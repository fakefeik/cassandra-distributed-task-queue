﻿using System;
using System.Collections.Generic;
using System.Linq;

using MoreLinq;

using NUnit.Framework;

using RemoteQueue.Cassandra.Entities;
using RemoteQueue.Cassandra.Repositories.BlobStorages;

using SKBKontur.Cassandra.CassandraClient.Abstractions;
using SKBKontur.Catalogue.Objects.TimeBasedUuid;

namespace FunctionalTests.RepositoriesTests
{
    public class TaskExceptionInfoStorageTest : BlobStorageFunctionalTestBase
    {
        [SetUp]
        public void SetUp()
        {
            taskExceptionInfoStorage = Container.Get<ITaskExceptionInfoStorage>();
        }

        [Test]
        public void Read_NoExceptions()
        {
            var taskMetas = new[]
                {
                    TimeGuidMeta(),
                    TimeGuidMeta().With(x => x.TaskExceptionInfoIds = new List<TimeGuid>()),
                    TimeGuidMeta().With(x => x.TaskExceptionInfoIds = new List<TimeGuid> {TimeGuid.NowGuid()}),
                    LegacyMeta()
                };

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(taskMetas[0], new Exception[0]),
                    new Tuple<TaskMetaInformation, Exception[]>(taskMetas[1], new Exception[0]),
                    new Tuple<TaskMetaInformation, Exception[]>(taskMetas[2], new Exception[0]),
                    new Tuple<TaskMetaInformation, Exception[]>(taskMetas[3], new Exception[0])
                });
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void TryAddDuplicate(MetaType metaType)
        {
            var exception = new Exception("Message");
            var duplicate = new Exception("Message");
            var meta = NewMeta(metaType);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception, out ids), Is.True);
            Assert.That(ids.Count, Is.EqualTo(1));

            List<TimeGuid> ids2;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta.With(x => x.TaskExceptionInfoIds = ids), duplicate, out ids2), Is.False);
            Assert.That(ids2, Is.Null);

            Check(new[] {new Tuple<TaskMetaInformation, Exception[]>(meta, new[] {exception})});
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void TryAddDuplicate_OnlyLastExceptionConsidered(MetaType metaType)
        {
            var exception1 = new Exception("Message");
            var exception2 = new Exception("Message-2");
            var exception3 = new Exception("Message");
            var meta = NewMeta(metaType);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception1, out ids), Is.True);
            Assert.That(ids.Count, Is.EqualTo(1));

            List<TimeGuid> ids2;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta.With(x => x.TaskExceptionInfoIds = ids), exception2, out ids2), Is.True);
            Assert.That(ids2.Count, Is.EqualTo(2));

            List<TimeGuid> ids3;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta.With(x => x.TaskExceptionInfoIds = ids2), exception3, out ids3), Is.True);
            Assert.That(ids3.Count, Is.EqualTo(3));

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta.With(x => x.TaskExceptionInfoIds = ids3),
                                                                metaType == MetaType.TimeGuid ? new[] {exception1, exception2, exception3} : new[] {exception3})
                });
        }

        [Test]
        public void Read_Normal()
        {
            var random = new Random(Guid.NewGuid().GetHashCode());

            var metasWithExceptions = new List<Tuple<TaskMetaInformation, Exception[]>>();
            for(var i = 0; i < 100; i++)
            {
                var randomValue = random.Next(0, 2);
                var meta = randomValue == 0 ? TimeGuidMeta() : LegacyMeta();
                var exceptions = new List<Exception>();
                for(var j = 0; j < 20; j++)
                {
                    var e = new Exception("Message-" + Guid.NewGuid().ToString("N"));
                    List<TimeGuid> ids;
                    Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, e, out ids), Is.True);
                    exceptions.Add(e);
                    meta.TaskExceptionInfoIds = ids;
                }
                metasWithExceptions.Add(new Tuple<TaskMetaInformation, Exception[]>(meta, randomValue == 0 ? exceptions.ToArray() : new[] {exceptions.Last()}));
            }

            Check(metasWithExceptions.ToArray());
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void Read_DuplicateMetas(MetaType metaType)
        {
            var exception = new Exception("Message");
            var meta = NewMeta(metaType);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta, new[] {exception}),
                    new Tuple<TaskMetaInformation, Exception[]>(meta, new[] {exception}),
                    new Tuple<TaskMetaInformation, Exception[]>(meta, new[] {exception}),
                });
        }

        [TestCase(MetaType.TimeGuid)]
        [TestCase(MetaType.Legacy)]
        public void Delete(MetaType metaType)
        {
            var exception1 = new Exception("Message");
            var exception2 = new Exception("Message-2");
            var meta = NewMeta(metaType);

            List<TimeGuid> ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception1, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;
            Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, exception2, out ids), Is.True);
            meta.TaskExceptionInfoIds = ids;

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta, metaType == MetaType.TimeGuid ? new[] {exception1, exception2} : new[] {exception2})
                });

            taskExceptionInfoStorage.Delete(meta);

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta, new Exception[] {})
                });
        }

        [Test]
        public void Achieve_Limit()
        {
            var meta = TimeGuidMeta();
            var exceptions = new List<Exception>();

            for(var i = 0; i < 300; i++)
            {
                var e = new Exception("Message-" + Guid.NewGuid().ToString("N"));
                List<TimeGuid> ids;
                Assert.That(taskExceptionInfoStorage.TryAddNewExceptionInfo(meta, e, out ids), Is.True);
                meta.TaskExceptionInfoIds = ids;
                exceptions.Add(e);
            }

            Check(new[]
                {
                    new Tuple<TaskMetaInformation, Exception[]>(meta, exceptions.Take(100).Concat(exceptions.Skip(199)).ToArray()), 
                });
        }

        private void Check(Tuple<TaskMetaInformation, Exception[]>[] expected)
        {
            var taskExceptionInfos = taskExceptionInfoStorage.Read(expected.Select(x => x.Item1).ToArray());

            Assert.That(taskExceptionInfos.Count, Is.EqualTo(expected.DistinctBy(x => x.Item1.Id).Count()));
            foreach(var tuple in expected)
            {
                Assert.That(taskExceptionInfos[tuple.Item1.Id].Select(info => info.ExceptionMessageInfo).ToArray(),
                            Is.EqualTo(tuple.Item2.Select(exception => exception.ToString()).ToArray()));
            }
        }

        private static TaskMetaInformation TimeGuidMeta()
        {
            return new TaskMetaInformation("Name-" + Guid.NewGuid().ToString("N"), TimeGuid.NowGuid().ToGuid().ToString());
        }

        private static TaskMetaInformation LegacyMeta()
        {
            return new TaskMetaInformation("Name-" + Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString());
        }

        private static TaskMetaInformation NewMeta(MetaType metaType)
        {
            return metaType == MetaType.TimeGuid ? TimeGuidMeta() : LegacyMeta();
        }

        protected override ColumnFamily[] GetColumnFamilies()
        {
            return TaskExceptionInfoStorage.GetColumnFamilyNames().Select(x => new ColumnFamily {Name = x}).ToArray();
        }

        public enum MetaType
        {
            TimeGuid,
            Legacy
        }

        private ITaskExceptionInfoStorage taskExceptionInfoStorage;
    }

    internal static class Sugar
    {
        public static T With<T>(this T obj, Action<T> action)
        {
            action(obj);
            return obj;
        }
    }
}