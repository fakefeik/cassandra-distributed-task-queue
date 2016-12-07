﻿using System;

using SKBKontur.Catalogue.ClientLib.Domains;
using SKBKontur.Catalogue.ClientLib.HttpClientBases;
using SKBKontur.Catalogue.ClientLib.HttpClientBases.Configuration;
using SKBKontur.Catalogue.ClientLib.Topology;

namespace TestCommon
{
    public class ElasticMonitoringServiceClient : HttpClientBase
    {
        public ElasticMonitoringServiceClient(IDomainTopologyFactory domainTopologyFactory, IMethodDomainFactory methodDomainFactory, IHttpServiceClientConfiguration configuration)
            : base(domainTopologyFactory, methodDomainFactory, configuration)
        {
        }

        public void UpdateAndFlush()
        {
            Method("UpdateAndFlush").SendToEachReplica(DomainConsistencyLevel.All);
        }

        public void ResetState()
        {
            Method("ResetState").SendToEachReplica(DomainConsistencyLevel.All);
        }

        protected override IHttpServiceClientConfiguration DoGetConfiguration(IHttpServiceClientConfiguration defaultConfiguration)
        {
            return defaultConfiguration.WithTimeout(TimeSpan.FromMinutes(1));
        }

        protected override string GetDefaultTopologyFileName()
        {
            return "elasticRemoteTaskQueueMonitoring";
        }
    }
}