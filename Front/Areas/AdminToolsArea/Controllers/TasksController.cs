﻿using RemoteQueue.Configuration;

using SKBKontur.Catalogue.Core.Web.Controllers;
using SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.MvcControllers.Controllers;

namespace SKBKontur.Catalogue.RemoteTaskQueue.Front.Areas.AdminToolsArea.Controllers
{
    public class TasksController : TasksBaseController
    {
        public TasksController(ControllerBaseParameters baseParameters, TasksControllerImpl controllerImpl, ITaskDataRegistry taskDataRegistry)
            : base(baseParameters, controllerImpl, taskDataRegistry)
        {
        }

        protected override string GetAdminToolsActions()
        {
            return Url.Action("Debug", "Default");
        }

        protected override bool CurrentUserHasAccessToReadAction()
        {
            return true;
        }

        protected override bool CurrentUserHasAccessToTaskData()
        {
            return true;
        }

        protected override bool CurrentUserHasAccessToWriteAction()
        {
            return true;
        }
    }
}