﻿using System.Collections.Generic;

using RemoteQueue.Cassandra.Entities;

namespace SKBKontur.Catalogue.RemoteTaskQueue.MonitoringServiceCore.Implementation
{
    public interface IEventCache
    {
        void AddEvents(IEnumerable<TaskMetaUpdatedEvent> events);
        void RemoveEvents(long threshold);
        void RemoveAll();
        bool Contains(TaskMetaUpdatedEvent elmentaryEvent);
        int GetCount();
    }
}