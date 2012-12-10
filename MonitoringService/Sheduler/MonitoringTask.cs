using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Repositories;
using RemoteQueue.Cassandra.Repositories.BlobStorages;
using RemoteQueue.Cassandra.Repositories.Indexes.StartTicksIndexes;

using SKBKontur.Catalogue.Core.SynchronizationStorage.LocalStorage;
using SKBKontur.Catalogue.RemoteTaskQueue.MonitoringService.Implementation;

using log4net;

namespace SKBKontur.Catalogue.RemoteTaskQueue.MonitoringService.Sheduler
{
    public class MonitoringTask : IMonitoringTask
    {
        public MonitoringTask(ITaskMetaInformationBlobStorage taskMetaInformationBlobStorage, ITaskMinimalStartTicksIndex taskMinimalStartTicksIndex, ILocalStorage localStorage)
        {
            this.localStorage = localStorage;
            handleTasksMetaStorage = new HandleTasksMetaStorage(taskMetaInformationBlobStorage, taskMinimalStartTicksIndex);
            lastTicks = 0;
        }

        public string Id { get { return "MonitoringTask"; } }

        public void Run()
        {
            var updatedTasksIds = handleTasksMetaStorage.GetAllTasksInStatesFromTicks(lastTicks,
                                                                                      TaskState.Canceled,
                                                                                      TaskState.Fatal,
                                                                                      TaskState.Finished,
                                                                                      TaskState.InProcess,
                                                                                      TaskState.New,
                                                                                      TaskState.Unknown,
                                                                                      TaskState.WaitingForRerun,
                                                                                      TaskState.WaitingForRerunAfterError).ToArray();
            // note �� ���������� �����?
            Console.WriteLine("Updated tasks ids count = " + updatedTasksIds.Count());
            if (updatedTasksIds.Count() == 0)
                return;
            var updatedTasksMetas = updatedTasksIds.Select(x => handleTasksMetaStorage.GetMeta(x));
            lastTicks = updatedTasksMetas.Max(x => x.MinimalStartTicks) + 1;
            var fid = updatedTasksIds.FirstOrDefault();
            var meta = handleTasksMetaStorage.GetMeta(fid);
            Console.WriteLine(meta.State);
            var hs = new Dictionary<string, TaskMetaInformationBusinessObjectWrap>();
            foreach (var id in updatedTasksIds)
                if (hs.ContainsKey(id))
                    hs[id].Info = handleTasksMetaStorage.GetMeta(id);
                else
                    hs.Add(id, new TaskMetaInformationBusinessObjectWrap
                    {
                        Id = id,
                        ScopeId = id,
                        Info = handleTasksMetaStorage.GetMeta(id)
                    });
            localStorage.Write(hs.Select(x => x.Value).ToArray());
        }

        private readonly ILocalStorage localStorage;

        private long lastTicks;

        private readonly IHandleTasksMetaStorage handleTasksMetaStorage;
        private readonly ILog logger = LogManager.GetLogger(typeof(MonitoringTask));
    }
}