﻿using System;
using System.Linq;

using RemoteTaskQueue.FunctionalTests.Common.ConsumerStateImpl;
using RemoteTaskQueue.FunctionalTests.Common.TaskDatas;

using SkbKontur.Cassandra.DistributedTaskQueue.Handling;

namespace ExchangeService.UserClasses
{
    public class FakeMixedPeriodicAndFailTaskHandler : RtqTaskHandler<FakeMixedPeriodicAndFailTaskData>
    {
        public FakeMixedPeriodicAndFailTaskHandler(ITestCounterRepository testCounterRepository)
        {
            this.testCounterRepository = testCounterRepository;
        }

        protected override HandleResult HandleTask(FakeMixedPeriodicAndFailTaskData taskData)
        {
            var decrementCounter = testCounterRepository.DecrementCounter(Context.Id);
            if (decrementCounter == 0)
                return Finish();
            if (taskData.FailCounterValues != null && taskData.FailCounterValues.Contains(decrementCounter + 1))
                return RerunAfterError(new Exception("Exception-" + Guid.NewGuid()), taskData.RerunAfter);
            else
                return Rerun(taskData.RerunAfter);
        }

        private readonly ITestCounterRepository testCounterRepository;
    }
}