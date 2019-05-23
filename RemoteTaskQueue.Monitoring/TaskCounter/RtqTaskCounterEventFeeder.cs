﻿using GroBuf;

using JetBrains.Annotations;

using RemoteQueue.Cassandra.Repositories;
using RemoteQueue.Cassandra.Repositories.GlobalTicksHolder;
using RemoteQueue.Configuration;

using SKBKontur.Catalogue.Core.EventFeeds;
using SKBKontur.Catalogue.Core.EventFeeds.Building;
using SKBKontur.Catalogue.Objects.Json;

using SkbKontur.Graphite.Client;

using Vostok.Logging.Abstractions;

namespace RemoteTaskQueue.Monitoring.TaskCounter
{
    public class RtqTaskCounterEventFeeder
    {
        public RtqTaskCounterEventFeeder(ILog logger,
                                         ISerializer serializer,
                                         IStatsDClient statsDClient,
                                         IGraphiteClient graphiteClient,
                                         ITaskDataRegistry taskDataRegistry,
                                         IRtqTaskCounterStateStorage stateStorage,
                                         EventFeedFactory eventFeedFactory,
                                         RtqTaskCounterSettings settings,
                                         RemoteQueue.Handling.RemoteTaskQueue remoteTaskQueue)
        {
            this.serializer = serializer;
            this.graphiteClient = graphiteClient;
            this.taskDataRegistry = taskDataRegistry;
            this.stateStorage = stateStorage;
            this.eventFeedFactory = eventFeedFactory;
            this.settings = settings;
            GlobalTime = remoteTaskQueue.GlobalTime;
            globalTimeProvider = new RtqGlobalTimeProvider(GlobalTime);
            eventLogRepository = remoteTaskQueue.EventLogRepository;
            handleTasksMetaStorage = remoteTaskQueue.HandleTasksMetaStorage;
            perfGraphiteReporter = new RtqMonitoringPerfGraphiteReporter("SubSystem.RemoteTaskQueue.TaskCounter.Perf", statsDClient);
            this.logger = logger.ForContext("CassandraDistributedTaskQueue").ForContext(nameof(RtqTaskCounterEventFeeder));
            this.logger.Info($"Using RtqTaskCounterSettings: {settings.ToPrettyJson()}");
        }

        [NotNull]
        public IGlobalTime GlobalTime { get; }

        public ( /*[NotNull]*/ IEventFeedsRunner, /*[NotNull]*/ RtqTaskCounterStateManager, /*[NotNull]*/ RtqTaskCounterGraphiteReporter) RunEventFeeding()
        {
            var stateManager = new RtqTaskCounterStateManager(logger, serializer, taskDataRegistry, stateStorage, settings, offsetInterpreter, perfGraphiteReporter);
            var eventConsumer = new RtqTaskCounterEventConsumer(stateManager, handleTasksMetaStorage, perfGraphiteReporter);
            IBladesBuilder<string> bladesBuilder = BladesBuilder.New(eventLogRepository, eventConsumer, logger);
            foreach (var bladeId in stateManager.Blades)
                bladesBuilder = bladesBuilder.WithBlade(bladeId.BladeKey, bladeId.Delay);
            var eventFeedsRunner = eventFeedFactory
                                   .WithOffsetType<string>()
                                   .WithEventType(bladesBuilder)
                                   .WithGlobalTimeProvider(globalTimeProvider)
                                   .WithOffsetInterpreter(offsetInterpreter)
                                   .WithOffsetStorageFactory(bladeId => stateManager.CreateOffsetStorage(bladeId))
                                   .WithSingleLeaderElectionKey(stateManager.CompositeFeedKey)
                                   .RunFeeds(settings.DelayBetweenEventFeedingIterations);
            return (eventFeedsRunner, stateManager, new RtqTaskCounterGraphiteReporter(stateManager, graphiteClient));
        }

        private readonly ILog logger;
        private readonly ISerializer serializer;
        private readonly IGraphiteClient graphiteClient;
        private readonly ITaskDataRegistry taskDataRegistry;
        private readonly IRtqTaskCounterStateStorage stateStorage;
        private readonly EventFeedFactory eventFeedFactory;
        private readonly RtqTaskCounterSettings settings;
        private readonly RtqGlobalTimeProvider globalTimeProvider;
        private readonly EventLogRepository eventLogRepository;
        private readonly IHandleTasksMetaStorage handleTasksMetaStorage;
        private readonly RtqMonitoringPerfGraphiteReporter perfGraphiteReporter;
        private readonly RtqEventLogOffsetInterpreter offsetInterpreter = new RtqEventLogOffsetInterpreter();
    }
}