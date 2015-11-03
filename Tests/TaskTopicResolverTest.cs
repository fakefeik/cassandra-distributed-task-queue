using System.Collections.Generic;

using NUnit.Framework;

using RemoteQueue.Handling;

using SKBKontur.Catalogue.RemoteTaskQueue.Common.RemoteTaskQueue;

namespace RemoteQueue.Tests
{
    [TestFixture]
    public class TaskTopicResolverTest
    {
        [Test]
        public void GetAllTaskTopics()
        {
            Assert.That(taskTopicResolver.GetAllTaskTopics().Length, Is.LessThanOrEqualTo(8));
            Assert.That(taskTopicResolver.GetAllTaskTopics(), Is.EquivalentTo(new[] {"0", "1", "2", "3", "4", "5", "6"}));
        }

        [Test]
        public void GetTaskTopic()
        {
            Assert.That(taskTopicResolver.GetTaskTopic("SimpleTaskData"), Is.EqualTo("1"));
            Assert.Throws<KeyNotFoundException>(() => taskTopicResolver.GetTaskTopic("UnregisteredTask"));
        }

        private readonly TaskTopicResolver taskTopicResolver = new TaskTopicResolver(new TaskDataTypeToNameMapper(new TaskDataRegistry()));
    }
}