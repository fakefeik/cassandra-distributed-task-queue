using System;
using System.Collections.Generic;
using System.Linq;

using log4net;

using MoreLinq;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Repositories;
using RemoteQueue.Cassandra.Repositories.GlobalTicksHolder;

using SKBKontur.Catalogue.Core.SynchronizationStorage.EventDevourers;
using SKBKontur.Catalogue.Core.SynchronizationStorage.LocalStorage;
using SKBKontur.Catalogue.RemoteTaskQueue.MonitoringDataTypes.MonitoringEntities;
using SKBKontur.Catalogue.RemoteTaskQueue.MonitoringServiceCore.Sheduler;

using MTaskState = SKBKontur.Catalogue.RemoteTaskQueue.MonitoringDataTypes.MonitoringEntities.Primitives.TaskState;

namespace SKBKontur.Catalogue.RemoteTaskQueue.MonitoringServiceCore.Implementation
{
    public class LocalStorageUpdater : ILocalStorageUpdater
    {
        public LocalStorageUpdater
            (IHandleTasksMetaStorage handleTasksMetaStorage,
             IEventLogRepository eventLogRepository,
             ILocalStorage localStorage,
             IGlobalTime globalTime)
        {
            this.handleTasksMetaStorage = handleTasksMetaStorage;
            this.eventLogRepository = eventLogRepository;
            this.localStorage = localStorage;
            this.globalTime = globalTime;
            processedEvents = new ProcessedEvents();
        }

        public void Update()
        {
            lock(lockObject)
            {
                var lastTicks = globalTime.GetNowTicks();
                UpdateLocalStorage(eventLogRepository.GetEvents(GetStartTime()));
                UpdateLocalStorageTicks(lastTicks);
            }
        }

        public void ClearProcessedEvents()
        {
            lock(lockObject)
            {
                processedEvents.Clear();
                globalTime.ResetInMemoryState();
            }
        }

        public void RecalculateInProcess()
        {
            lock(lockObject)
            {
                var metadatas = localStorage.Search<MonitoringTaskMetadata>(
                    x => x.State == MTaskState.New ||
                         x.State == MTaskState.InProcess ||
                         x.State == MTaskState.WaitingForRerun ||
                         x.State == MTaskState.WaitingForRerunAfterError).ToArray();
                var list = new List<MonitoringTaskMetadata>();
                var metas = handleTasksMetaStorage.GetMetas(metadatas.Select(x => x.TaskId).ToArray()).Values;
                foreach(var meta in metas)
                {
                    MonitoringTaskMetadata newMetadata;
                    if(TryConvertTaskMetaInformationToMonitoringTaskMetadata(meta, out newMetadata))
                        list.Add(newMetadata);
                }
                foreach(var batch in new SeparateOnBatchesEnumerable<MonitoringTaskMetadata>(list, 100))
                    localStorage.Write(batch, false);
            }
        }

        public long GetLastUpdateTime()
        {
            return localStorage.GetLastUpdateTime<MonitoringTaskMetadata>();
        }

        private void UpdateLocalStorage(IEnumerable<TaskMetaUpdatedEvent> events)
        {
            var batchCount = 0;
            foreach(var eventBatch in events.Batch(1000, Enumerable.ToArray))
            {
                logger.InfoFormat("Reading batch #{0} with {1} events", batchCount++, eventBatch.Length);

                var uniqueEventBatch = eventBatch
                    .Where(@event => !processedEvents.Contains(@event))
                    .GroupBy(x => x.TaskId)
                    .Select(x => x.MinBy(y => y.Ticks))
                    .ToArray();
                var eventsWithNotEmptyTaskId = uniqueEventBatch.Where(x => !string.IsNullOrEmpty(x.TaskId)).ToArray();
                if(eventsWithNotEmptyTaskId.Length < uniqueEventBatch.Length)
                    logger.Error("Some events has taskId=[null]");
                uniqueEventBatch = eventsWithNotEmptyTaskId;

                var taskMetas = handleTasksMetaStorage.GetMetas(uniqueEventBatch.Select(x => x.TaskId).ToArray());
                if(uniqueEventBatch.Length > taskMetas.Count)
                    logger.WarnFormat("Lost {0} task metas", uniqueEventBatch.Length - taskMetas.Count);

                var list = new List<MonitoringTaskMetadata>();
                var localProcessedEvents = new List<TaskMetaUpdatedEvent>();

                foreach(var taskEvent in uniqueEventBatch)
                {
                    try
                    {
                        TaskMetaInformation taskMeta;
                        if(!taskMetas.TryGetValue(taskEvent.TaskId, out taskMeta))
                        {
                            logger.WarnFormat("Cannot read meta for '{0}'", taskEvent.TaskId);
                            continue;
                        }

                        if(taskMeta.LastModificationTicks == null)
                        {
                            logger.WarnFormat("TaskMeta with id='{0}' have LastModificationTicks==[null]", taskEvent.TaskId);
                            continue;
                        }

                        if(taskEvent.Ticks > taskMeta.LastModificationTicks)
                        {
                            logger.InfoFormat("TaskMeta with id='{0}' have too old LastModificationTicks", taskEvent.TaskId);
                            continue;
                        }

                        MonitoringTaskMetadata metadata;
                        if(!TryConvertTaskMetaInformationToMonitoringTaskMetadata(taskMeta, out metadata))
                        {
                            logger.WarnFormat("Error while index metadata for task '{0}'", taskMeta.Id);
                            continue;
                        }

                        list.Add(metadata);
                        localProcessedEvents.Add(taskEvent);
                    }
                    catch(Exception e)
                    {
                        logger.Error(string.Format("Error while processing taskEvent taskId='{0}'", taskEvent.TaskId), e);
                    }
                }
                processedEvents.RemoveEvents(eventBatch.Min(@event => @event.Ticks) - TimeSpan.FromMinutes(20).Ticks);

                foreach(var batch in list.Batch(100, Enumerable.ToArray))
                    localStorage.Write(batch, false);
                logger.InfoFormat("Wrote {0} rows in sql", list.Count);

                UpdateLocalStorageTicks(eventBatch.Last().Ticks);
                processedEvents.AddEvents(localProcessedEvents);
            }
        }

        private void UpdateLocalStorageTicks(long lastTicks)
        {
            lock(lockObject)
            {
                if(localStorage.GetLastUpdateTime<MonitoringTaskMetadata>() < lastTicks)
                    localStorage.SetLastUpdateTime<MonitoringTaskMetadata>(lastTicks);
            }
        }

        private long GetStartTime()
        {
            return GetLastUpdateTime() - eventLogRepository.UnstableZoneLength.Ticks;
        }

        private bool TryConvertTaskMetaInformationToMonitoringTaskMetadata(TaskMetaInformation info, out MonitoringTaskMetadata taskMetadata)
        {
            if(info == null)
            {
                taskMetadata = new MonitoringTaskMetadata();
                logger.Error("MetaInformation null");
                return false;
            }
            DateTime minimalStartTicks;
            if(info.MinimalStartTicks < DateTime.MinValue.Ticks)
                minimalStartTicks = DateTime.MinValue;
            else if(info.MinimalStartTicks > DateTime.MaxValue.Ticks)
                minimalStartTicks = DateTime.MaxValue;
            else
                minimalStartTicks = new DateTime(info.MinimalStartTicks);
            taskMetadata = new MonitoringTaskMetadata
                {
                    Name = info.Name,
                    TaskId = info.Id,
                    Ticks = new DateTime(info.Ticks),
                    MinimalStartTicks = minimalStartTicks,
                    StartExecutingTicks = NullableTickToNullableDateTime(info.StartExecutingTicks),
                    FinishExecutingTicks = NullableTickToNullableDateTime(info.FinishExecutingTicks),
                    LastModificationDateTime = NullableTickToNullableDateTime(info.LastModificationTicks),
                    State = default(MTaskState),
                    Attempts = info.Attempts,
                    ParentTaskId = info.ParentTaskId,
                    TaskGroupLock = info.TaskGroupLock,
                };
            MTaskState mtaskState;
            if(!Enum.TryParse(info.State.ToString(), true, out mtaskState))
            {
                logger.ErrorFormat("�� ���� ��������������� TaskState(RemouteTaskQueue) � TaskState(MonitoringDataTypes). TaskId: {0}", taskMetadata.TaskId); // todo �������� ���������� ���������
                return false;
            }
            taskMetadata.State = mtaskState;
            return true;
        }

        private static DateTime? NullableTickToNullableDateTime(long? value)
        {
            return value.HasValue ? (DateTime?)new DateTime(value.Value) : null;
        }

        private readonly ILog logger = LogManager.GetLogger(typeof(MonitoringTask));
        private readonly IHandleTasksMetaStorage handleTasksMetaStorage;
        private readonly IEventLogRepository eventLogRepository;
        private readonly ILocalStorage localStorage;
        private readonly IGlobalTime globalTime;
        private readonly IProcessedEvents processedEvents;
        private readonly object lockObject = new object();
    }
}