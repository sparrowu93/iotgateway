﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using WalkingTec.Mvvm.Core;
using WalkingTec.Mvvm.Core.Extensions;
using IoTGateway.Model;
using Plugin;

namespace IoTGateway.ViewModel.Config.SystemConfigVMs
{
    public partial class SystemConfigVM : BaseCRUDVM<SystemConfig>
    {

        public SystemConfigVM()
        {
        }

        protected override void InitVM()
        {
        }

        public override void DoAdd()
        {           
            base.DoAdd();
        }

        public override void DoEdit(bool updateAllFields = false)
        {
            base.DoEdit(updateAllFields);
            var messageService = Wtm.ServiceProvider.GetService(typeof(MessageService)) as MessageService;
            messageService.StartClientAsync().Wait();
        }

        public override void DoDelete()
        {
            base.DoDelete();
        }
    }
}
