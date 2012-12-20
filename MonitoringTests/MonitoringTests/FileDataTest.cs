﻿using System;
using System.Text;

using NUnit.Framework;

using RemoteQueue.Handling;

using SKBKontur.Catalogue.CassandraStorageCore.FileDataStorage;
using SKBKontur.Catalogue.RemoteTaskQueue.MonitoringTests.TestBases;
using SKBKontur.Catalogue.RemoteTaskQueue.TaskDatas;

namespace SKBKontur.Catalogue.RemoteTaskQueue.MonitoringTests.MonitoringTests
{
    public class FileDataTest : MonitoringFunctionalTestBase
    {
        public override void SetUp()
        {
            base.SetUp();
            remoteTaskQueue = container.Get<IRemoteTaskQueue>();
            fileDataStorage = container.Get<FileDataStorage>();
        }

        [Test, Ignore]
        public void TestByteArray()
        {
            CreateUser("user", "psw");
            AddTask(new ByteArrayTaskData
                {
                    Bytes = Encoding.UTF8.GetBytes("Test")
                });
        }

        [Test, Ignore]
        public void TestFileId()
        {
            CreateUser("user", "psw");
            var id = Guid.NewGuid().ToString();
            fileDataStorage.Write(id, new FileData
                {
                    Filename = "Filename",
                    Content = Encoding.UTF8.GetBytes("Testtesttest")
                });
            AddTask(new FileIdTaskData
                {
                    FileId = id
                });
        }

        private string AddTask<T>(T taskData) where T : ITaskData
        {
            return remoteTaskQueue.Queue(taskData);
        }

        private IRemoteTaskQueue remoteTaskQueue;
        private FileDataStorage fileDataStorage;
    }
}