﻿using System;
using System.Linq;

using GroBuf;

using JetBrains.Annotations;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Primitives;
using RemoteQueue.Cassandra.Repositories;
using RemoteQueue.Cassandra.Repositories.BlobStorages;
using RemoteQueue.Cassandra.Repositories.GlobalTicksHolder;
using RemoteQueue.Cassandra.Repositories.Indexes.ChildTaskIndex;
using RemoteQueue.Cassandra.Repositories.Indexes.StartTicksIndexes;
using RemoteQueue.Configuration;
using RemoteQueue.Handling.ExecutionContext;
using RemoteQueue.LocalTasks.TaskQueue;
using RemoteQueue.Profiling;
using RemoteQueue.Settings;

using SKBKontur.Cassandra.CassandraClient.Clusters;
using SKBKontur.Catalogue.CassandraPrimitives.RemoteLock;
using SKBKontur.Catalogue.CassandraPrimitives.RemoteLock.RemoteLocker;
using SKBKontur.Catalogue.CassandraPrimitives.Storages.Primitives;
using SKBKontur.Catalogue.Objects.TimeBasedUuid;

namespace RemoteQueue.Handling
{
    public class RemoteTaskQueue : IRemoteTaskQueue, IRemoteTaskQueueInternals
    {
        public RemoteTaskQueue(
            ISerializer serializer,
            ICassandraCluster cassandraCluster,
            ICassandraSettings cassandraSettings,
            IRemoteTaskQueueSettings taskQueueSettings,
            ITaskDataRegistry taskDataRegistry,
            IRemoteTaskQueueProfiler remoteTaskQueueProfiler)
        {
            this.taskDataRegistry = taskDataRegistry;
            Serializer = serializer;
            enableContinuationOptimization = taskQueueSettings.EnableContinuationOptimization;
            var parameters = new ColumnFamilyRepositoryParameters(cassandraCluster, cassandraSettings);
            var ticksHolder = new TicksHolder(serializer, parameters);
            GlobalTime = new GlobalTime(ticksHolder);
            TaskMinimalStartTicksIndex = new TaskMinimalStartTicksIndex(parameters, serializer, GlobalTime, new OldestLiveRecordTicksHolder(ticksHolder));
            var taskMetaInformationBlobStorage = new TaskMetaInformationBlobStorage(parameters, serializer, GlobalTime);
            var eventLongRepository = new EventLogRepository(serializer, GlobalTime, parameters, ticksHolder);
            childTaskIndex = new ChildTaskIndex(parameters, serializer, taskMetaInformationBlobStorage);
            HandleTasksMetaStorage = new HandleTasksMetaStorage(taskMetaInformationBlobStorage, TaskMinimalStartTicksIndex, eventLongRepository, GlobalTime, childTaskIndex, taskDataRegistry);
            HandleTaskCollection = new HandleTaskCollection(HandleTasksMetaStorage, new TaskDataBlobStorage(parameters, serializer, GlobalTime), remoteTaskQueueProfiler);
            TaskExceptionInfoStorage = new TaskExceptionInfoBlobStorage(parameters, serializer, GlobalTime);
            var remoteLockImplementationSettings = CassandraRemoteLockImplementationSettings.Default(new ColumnFamilyFullName(parameters.Settings.QueueKeyspace, parameters.LockColumnFamilyName));
            var remoteLockImplementation = new CassandraRemoteLockImplementation(cassandraCluster, serializer, remoteLockImplementationSettings);
            RemoteLockCreator = new RemoteLocker(remoteLockImplementation, new RemoteLockerMetrics(parameters.Settings.QueueKeyspace));
            RemoteTaskQueueProfiler = remoteTaskQueueProfiler;
        }

        public ITaskExceptionInfoBlobStorage TaskExceptionInfoStorage { get; private set; }
        public ISerializer Serializer { get; private set; }
        public IGlobalTime GlobalTime { get; private set; }
        public ITaskMinimalStartTicksIndex TaskMinimalStartTicksIndex { get; private set; }
        public IHandleTasksMetaStorage HandleTasksMetaStorage { get; private set; }
        public IHandleTaskCollection HandleTaskCollection { get; private set; }
        public IRemoteLockCreator RemoteLockCreator { get; private set; }
        public IRemoteTaskQueueProfiler RemoteTaskQueueProfiler { get; private set; }
        IRemoteTaskQueue IRemoteTaskQueueInternals.RemoteTaskQueue { get { return this; } }

        public bool CancelTask([NotNull] string taskId)
        {
            IRemoteLock remoteLock;
            if(!RemoteLockCreator.TryGetLock(taskId, out remoteLock))
                return false;
            using(remoteLock)
            {
                var meta = HandleTasksMetaStorage.GetMeta(taskId);
                if(meta.State == TaskState.New || meta.State == TaskState.WaitingForRerun || meta.State == TaskState.WaitingForRerunAfterError || meta.State == TaskState.InProcess)
                {
                    RemoteTaskQueueProfiler.ProcessTaskCancel(meta);
                    meta.State = TaskState.Canceled;
                    meta.FinishExecutingTicks = DateTime.UtcNow.Ticks;
                    HandleTasksMetaStorage.AddMeta(meta);
                    return true;
                }
                return false;
            }
        }

        public bool RerunTask([NotNull] string taskId, TimeSpan delay)
        {
            IRemoteLock remoteLock;
            if(!RemoteLockCreator.TryGetLock(taskId, out remoteLock))
                return false;
            using(remoteLock)
            {
                var meta = HandleTasksMetaStorage.GetMeta(taskId);
                meta.State = TaskState.WaitingForRerun;
                meta.MinimalStartTicks = DateTime.UtcNow.Ticks + delay.Ticks;
                HandleTasksMetaStorage.AddMeta(meta);
                return true;
            }
        }

        [NotNull]
        public RemoteTaskInfo GetTaskInfo([NotNull] string taskId)
        {
            return GetTaskInfos(new[] {taskId}).Single();
        }

        [NotNull]
        public RemoteTaskInfo<T> GetTaskInfo<T>([NotNull] string taskId)
            where T : ITaskData
        {
            return GetTaskInfos<T>(new[] {taskId}).Single();
        }

        [NotNull]
        public RemoteTaskInfo[] GetTaskInfos([NotNull] string[] taskIds)
        {
            var tasks = HandleTaskCollection.GetTasks(taskIds);
            var taskExceptionInfos = TaskExceptionInfoStorage.Read(tasks.Select(x => x.Meta.TaskExceptionId).ToArray());
            return tasks.Select(task => new RemoteTaskInfo
                {
                    Context = task.Meta,
                    TaskData = (ITaskData)Serializer.Deserialize(taskDataRegistry.GetTaskType(task.Meta.Name), task.Data),
                    ExceptionInfo = taskExceptionInfos.ContainsKey(task.Meta.TaskExceptionId) ? taskExceptionInfos[task.Meta.TaskExceptionId] : null
                }).ToArray();
        }

        [NotNull]
        public RemoteTaskInfo<T>[] GetTaskInfos<T>([NotNull] string[] taskIds) where T : ITaskData
        {
            return GetTaskInfos(taskIds).Select(ConvertRemoteTaskInfo<T>).ToArray();
        }

        [NotNull]
        public IRemoteTask CreateTask<T>([NotNull] T taskData, [CanBeNull] CreateTaskOptions createTaskOptions = null) where T : ITaskData
        {
            createTaskOptions = createTaskOptions ?? new CreateTaskOptions();
            var type = taskData.GetType();
            var taskId = TimeGuid.NowGuid().ToGuid().ToString();
            var task = new Task
                {
                    Data = Serializer.Serialize(type, taskData),
                    Meta = new TaskMetaInformation(taskDataRegistry.GetTaskName(type), taskId)
                        {
                            Attempts = 0,
                            Ticks = DateTime.UtcNow.Ticks,
                            ParentTaskId = string.IsNullOrEmpty(createTaskOptions.ParentTaskId) ? GetCurrentExecutingTaskId() : createTaskOptions.ParentTaskId,
                            TaskGroupLock = createTaskOptions.TaskGroupLock,
                            State = TaskState.New,
                            MinimalStartTicks = 0,
                        }
                };
            RemoteTaskQueueProfiler.ProcessTaskCreation(task.Meta, taskData);
            return enableContinuationOptimization && LocalTaskQueue.Instance != null
                       ? new RemoteTaskWithContinuationOptimization(task, HandleTaskCollection, LocalTaskQueue.Instance)
                       : new RemoteTask(task, HandleTaskCollection);
        }

        [CanBeNull]
        private static string GetCurrentExecutingTaskId()
        {
            var context = TaskExecutionContext.Current;
            if(context == null)
                return null;
            return context.CurrentTask.Meta.Id;
        }

        [NotNull]
        public string[] GetChildrenTaskIds([NotNull] string taskId)
        {
            return childTaskIndex.GetChildTaskIds(taskId);
        }

        [NotNull]
        private static RemoteTaskInfo<T> ConvertRemoteTaskInfo<T>([NotNull] RemoteTaskInfo task) where T : ITaskData
        {
            var taskType = task.TaskData.GetType();
            if(!typeof(T).IsAssignableFrom(taskType))
                throw new Exception(string.Format("Type '{0}' is not assignable from '{1}'", typeof(T).FullName, taskType.FullName));
            return new RemoteTaskInfo<T>
                {
                    Context = task.Context,
                    TaskData = (T)task.TaskData,
                    ExceptionInfo = task.ExceptionInfo,
                };
        }

        private readonly ITaskDataRegistry taskDataRegistry;
        private readonly IChildTaskIndex childTaskIndex;
        private readonly bool enableContinuationOptimization;
    }
}