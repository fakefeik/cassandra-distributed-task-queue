﻿using JetBrains.Annotations;

using SkbKontur.Cassandra.DistributedTaskQueue.Cassandra.Repositories.Indexes.StartTicksIndexes;

using SKBKontur.Catalogue.ServiceLib.Scheduling;

namespace SkbKontur.Cassandra.DistributedTaskQueue.Handling
{
    internal interface IHandlerManager : IPeriodicTask
    {
        [NotNull]
        LiveRecordTicksMarkerState[] GetCurrentLiveRecordTicksMarkers();
    }
}