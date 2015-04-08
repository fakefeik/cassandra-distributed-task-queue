﻿using System;
using System.Collections.Generic;

using Elasticsearch.Net;

using log4net;

using SKBKontur.Catalogue.Core.ElasticsearchClientExtensions;

namespace SKBKontur.Catalogue.RemoteTaskQueue.ElasticMonitoring.TaskIndexedStorage.Actualizer
{
    class IndexManager
    {
        private TaskSearchIndexSchema taskSearchIndexSchema;
        public void EnsureIndexCreated(long ticks)
        {
            taskSearchIndexSchema.CreateCurrentAliases(ticks);
        }
    }
    public class TaskSearchIndexSchema
    {
        public TaskSearchIndexSchema(
            IElasticsearchClientFactory elasticsearchClientFactory,
            TaskSearchDynamicSettings dynamicSettings)
        {
            this.dynamicSettings = dynamicSettings;
            elasticsearchClient = elasticsearchClientFactory.GetClient();
        }

        public const string LastUpdateTicksIndex = "lastupdate-monitoringsearch";
        public const string LastUpdateTicksType = "LastUpdateTicks";
        public const string IndexPrefix = "monitoringsearch-";
        public const string SearchPrefix = "msearch-";
        public const string PutPrefix = "mput-";
        public const string OldDataIndex = IndexPrefix + "OldData";
        public const string AllDataIndicesWildcard = IndexPrefix + "*";
        public const string IndexTemplateName = "monitoringsearch-template";

        public void DeleteAll()
        {
            elasticsearchClient.IndicesDelete(LastUpdateTicksIndex).ProcessResponse(200, 404);
            //todo bug разрушает индексы
            //elasticsearchClient.IndicesDelete(AllIndexWildcard).ProcessResponse(200, 404);

            var searchIndices = FindIndices(AllDataIndicesWildcard);

            foreach(var searchIndex in searchIndices)
            {
                var mapping = elasticsearchClient.IndicesGetMapping<Dictionary<String, MapingItem>>(searchIndex).ProcessResponse();
                var types = mapping.Response[searchIndex].mappings.Keys;
                foreach(var type in types)
                    elasticsearchClient.DeleteByQuery(searchIndex, type, new {query = new {match_all = new {}}}).ProcessResponse();
            }

            elasticsearchClient.IndicesDeleteTemplateForAll(IndexTemplateName).ProcessResponse(200, 404);

            Refresh();
        }

        private string[] FindIndices(string template)
        {
            var indices = elasticsearchClient.CatIndices(template).ProcessResponse();
            return Parse(indices.Response);
        }

        private static string[] Parse(string s)
        {
            var strings = s.Split(new[] {"\n"}, StringSplitOptions.None);
            var lst = new List<string>();
            foreach(var line in strings)
            {
                var split = line.Split(new[] {' ', '\t'}, StringSplitOptions.RemoveEmptyEntries);
                if(split.Length > 1)
                    lst.Add(split[2]);
            }
            return lst.ToArray();
        }

        public void Refresh()
        {
            elasticsearchClient.IndicesRefresh("_all");
        }

        private static string ToIsoTime(DateTime dt)
        {
            return dt.ToUniversalTime().ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK");
        }

        public void CreateCurrentAliases(long ticks)
        {
            var indexName = IndexNameFactory.BuildIndexNameForTime(IndexPrefix, ticks);
            var searchIndexAlias = IndexNameFactory.BuildIndexNameForTime(SearchPrefix, ticks);
            var putIndexAlias = IndexNameFactory.BuildIndexNameForTime(PutPrefix, ticks);

            elasticsearchClient.IndicesUpdateAliasesForAll(new
                {
                    actions = new object[]
                        {
                            new {add = new {index = indexName, alias = putIndexAlias}},
                            new {add = new {index = indexName, alias = searchIndexAlias}},
                        }
                }).ProcessResponse();
        }

        public void RetireIndex(long ticks)
        {
            var indexName = IndexNameFactory.BuildIndexNameForTime(IndexPrefix, ticks);
            var searchIndexAlias = IndexNameFactory.BuildIndexNameForTime(SearchPrefix, ticks);
            var putIndexAlias = IndexNameFactory.BuildIndexNameForTime(PutPrefix, ticks);
            DateTime beginDateInc;
            DateTime endDateExc;
            IndexNameFactory.GetDateRange(ticks, out beginDateInc, out endDateExc);
            elasticsearchClient.IndicesUpdateAliasesForAll(new
                {
                    actions = new object[]
                        {
                            new {add = new {index = OldDataIndex, alias = putIndexAlias}},
                            new
                                {
                                    add = new
                                        {
                                            index = OldDataIndex, alias = searchIndexAlias, filter = new
                                                {
                                                    range = new
                                                        {
                                                            EnqueueTime = new
                                                                {
                                                                    gte = ToIsoTime(beginDateInc),
                                                                    lt = ToIsoTime(endDateExc),
                                                                    format = dateFormat
                                                                }
                                                        }
                                                }
                                        }
                                },
                            new {remove = new {index = indexName, alias = putIndexAlias}},
                            new {remove = new {index = indexName, alias = searchIndexAlias}},
                        }
                }).ProcessResponse();
        }

        public void ActualizeTemplate()
        {
            var response = elasticsearchClient.IndicesGetTemplateForAll(IndexTemplateName).ProcessResponse(200, 404);
            logger.InfoFormat("TaskSearchIndexSchema: got response {0}", response.HttpStatusCode);
            if(response.HttpStatusCode == 404)
            {
                elasticsearchClient
                    .IndicesPutTemplateForAll(IndexTemplateName, new
                        {
                            template = AllDataIndicesWildcard,
                            settings = new
                                {
                                    number_of_shards = dynamicSettings.NumberOfShards,
                                    number_of_replicas = dynamicSettings.ReplicaCount,
                                },
                            mappings = new
                                {
                                    _default_ = new
                                        {
                                            _all = new {enabled = true},
                                            dynamic_templates = new object[]
                                                {
                                                    new
                                                        {
                                                            template_strings = new
                                                                {
                                                                    path_match = "Data.*",
                                                                    match_mapping_type = "string",
                                                                    mapping = StringTemplate()
                                                                },
                                                        },
                                                    new
                                                        {
                                                            template_dates = new
                                                                {
                                                                    path_match = "Data.*",
                                                                    match_mapping_type = "date",
                                                                    mapping = DateTemplate()
                                                                }
                                                        },
                                                    new
                                                        {
                                                            no_store = new
                                                                {
                                                                    path_match = "Data.*",
                                                                    mapping = new
                                                                        {
                                                                            store = "no"
                                                                        }
                                                                },
                                                        },
                                                },
                                            properties = new
                                                {
                                                    Meta = new
                                                        {
                                                            properties = new
                                                                {
                                                                    Name = StringTemplate(),
                                                                    Id = StringTemplate(),
                                                                    State = StringTemplate(),
                                                                    ParentTaskId = StringTemplate(),
                                                                    TaskGroupLock = StringTemplate(),
                                                                    Attempts = new {type = "integer"},
                                                                    EnqueueTime = DateTemplate(),
                                                                    MinimalStartTime = DateTemplate(),
                                                                    StartExecutingTime = DateTemplate(),
                                                                    FinishExecutingTime = DateTemplate(),
                                                                    LastModificationTime = DateTemplate(),
                                                                }
                                                        }
                                                }
                                        }
                                },
                        }
                    ).ProcessResponse();
                if(elasticsearchClient.IndicesExists(LastUpdateTicksIndex).ProcessResponse(200, 404).HttpStatusCode == 404)
                {
                    elasticsearchClient.
                        IndicesCreate(LastUpdateTicksIndex, new
                            {
                                settings = new
                                    {
                                        number_of_shards = dynamicSettings.NumberOfShards,
                                        number_of_replicas = dynamicSettings.ReplicaCount,
                                    },
                                mappings = new
                                    {
                                        LastUpdateTicks = new
                                            {
                                                _all = new {enabled = false},
                                                properties = new
                                                    {
                                                        Ticks = new
                                                            {
                                                                type = "long",
                                                                index = "no"
                                                            }
                                                    }
                                            }
                                    }
                            }).ProcessResponse();
                }

                logger.InfoFormat("TaskSearchIndexSchema: schema created");
            }
        }

        private static object DateTemplate()
        {
            return new {type = "date", format = dateFormat, store = "no"};
        }

        private static object StringTemplate()
        {
            return new {type = "string", store = "no", index = "not_analyzed"};
        }

        private const string dateFormat = "dateOptionalTime";

        private readonly TaskSearchDynamicSettings dynamicSettings;
        private readonly IElasticsearchClient elasticsearchClient;

        private static readonly ILog logger = LogManager.GetLogger("TaskSearchIndexSchema");

        private class MapingItem
        {
            public Dictionary<string, object> mappings { get; set; }
        }
    }
}