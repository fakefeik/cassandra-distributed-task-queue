﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using GroBuf;

using RemoteQueue.Handling;
using RemoteQueue.LocalTasks.TaskQueue;
using RemoteQueue.Profiling;
using RemoteQueue.Settings;

using SKBKontur.Cassandra.CassandraClient.Clusters;
using SKBKontur.Catalogue.ServiceLib.Scheduling;

using Vostok.Logging.Abstractions;

#pragma warning disable 618

namespace RemoteQueue.Configuration
{
    public class RtqConsumer : IDisposable
    {
        public RtqConsumer(ILog logger,
                           IRtqConsumerSettings consumerSettings,
                           IPeriodicTaskRunner periodicTaskRunner,
                           ITaskDataRegistry taskDataRegistry,
                           ITaskHandlerRegistry taskHandlerRegistry,
                           ISerializer serializer,
                           ICassandraCluster cassandraCluster,
                           IRemoteTaskQueueSettings taskQueueSettings,
                           IRemoteTaskQueueProfiler remoteTaskQueueProfiler)
        {
            this.consumerSettings = consumerSettings;
            this.periodicTaskRunner = periodicTaskRunner;
            var taskCounter = new TaskCounter(consumerSettings.MaxRunningTasksCount, consumerSettings.MaxRunningContinuationsCount);
            var remoteTaskQueue = new RemoteTaskQueue(logger, serializer, cassandraCluster, taskQueueSettings, taskDataRegistry, remoteTaskQueueProfiler);
            localTaskQueue = new LocalTaskQueue(taskCounter, taskHandlerRegistry, remoteTaskQueue);
            foreach (var taskTopic in taskHandlerRegistry.GetAllTaskTopicsToHandle())
                handlerManagers.Add(new HandlerManager(taskTopic, consumerSettings.MaxRunningTasksCount, localTaskQueue, remoteTaskQueue.HandleTasksMetaStorage, remoteTaskQueue.GlobalTime, logger));
            reportConsumerStateToGraphiteTask = new ReportConsumerStateToGraphiteTask(remoteTaskQueueProfiler, handlerManagers);
            RemoteTaskQueueBackdoor = remoteTaskQueue;
            this.logger = logger.ForContext("CassandraDistributedTaskQueue.Consumer");
        }

        public void Dispose()
        {
            Stop();
        }

        public void Start()
        {
            if (!started)
            {
                lock (lockObject)
                {
                    if (!started)
                    {
                        RemoteTaskQueueBackdoor.ResetTicksHolderInMemoryState();
                        localTaskQueue.Start();
                        foreach (var handlerManager in handlerManagers)
                            periodicTaskRunner.Register(handlerManager, consumerSettings.PeriodicInterval);
                        periodicTaskRunner.Register(reportConsumerStateToGraphiteTask, TimeSpan.FromMinutes(1));
                        started = true;
                        var handlerManagerIds = string.Join("\r\n", handlerManagers.Select(x => x.Id));
                        logger.Info($"Start RtqConsumer: schedule handlerManagers[{handlerManagers.Count}] with period {consumerSettings.PeriodicInterval}:\r\n{handlerManagerIds}");
                    }
                }
            }
        }

        public void Stop()
        {
            if (started)
            {
                lock (lockObject)
                {
                    if (started)
                    {
                        logger.Info("Stopping RtqConsumer");
                        periodicTaskRunner.Unregister(reportConsumerStateToGraphiteTask.Id, 15000);
                        Task.WaitAll(handlerManagers.Select(theHandlerManager => Task.Factory.StartNew(() => { periodicTaskRunner.Unregister(theHandlerManager.Id, 15000); })).ToArray());
                        localTaskQueue.StopAndWait(TimeSpan.FromSeconds(100));
                        RemoteTaskQueueBackdoor.ResetTicksHolderInMemoryState();
                        started = false;
                        logger.Info("RtqConsumer stopped");
                    }
                }
            }
        }

        [Obsolete("Only for usage in tests")]
        public IRemoteTaskQueueBackdoor RemoteTaskQueueBackdoor { get; }

        private volatile bool started;
        private readonly IRtqConsumerSettings consumerSettings;
        private readonly IPeriodicTaskRunner periodicTaskRunner;
        private readonly LocalTaskQueue localTaskQueue;
        private readonly ReportConsumerStateToGraphiteTask reportConsumerStateToGraphiteTask;
        private readonly ILog logger;
        private readonly object lockObject = new object();
        private readonly List<IHandlerManager> handlerManagers = new List<IHandlerManager>();
    }
}