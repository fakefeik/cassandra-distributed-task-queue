using SKBKontur.Catalogue.Core.Graphite.Client.Relay;
using SKBKontur.Catalogue.Core.Graphite.Client.StatsD;

namespace SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.Core.Implementation
{
    public class RtqElasticsearchIndexerGraphiteReporter : RtqElasticsearchIndexerGraphiteReporterBase
    {
        public RtqElasticsearchIndexerGraphiteReporter(ICatalogueGraphiteClient graphiteClient, ICatalogueStatsDClient statsDClient)
            : base("EDI.SubSystem.RemoteTaskQueue.ElasticsearchIndexer", graphiteClient, statsDClient)
        {
        }
    }
}