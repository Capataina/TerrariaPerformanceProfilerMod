/* ============================================================
   callgraph.js - written by upkeep-context callgraph_scan.py
   schema: cg1
   Script-owned file: re-runs regenerate it wholesale.
   ============================================================ */
window.CALLGRAPH = JSON.parse(`{
  "schema": "cg1",
  "lang": "csharp",
  "scope": "entry: InteractionNpc.OnSpawn, InteractionPlayer.OnHitNPC, InteractionPlayer.OnHitNPCWithItem, OverlayPanel.Update, ProfilerSystem.PostSetupContent, ProfilerSystem.PostUpdateEverything · csharp · 326 files · 1116 functions · also detected python: 78 fns",
  "stats": [
    [
      "functions",
      "1116",
      "",
      "in scope"
    ],
    [
      "call edges",
      "3372",
      "",
      "static"
    ],
    [
      "resolved",
      "1458",
      "ok",
      "43%"
    ],
    [
      "ambiguous",
      "686",
      "warn",
      "20%"
    ],
    [
      "external",
      "1228",
      "dim",
      "collapsed"
    ],
    [
      "dynamic",
      "0",
      "violet",
      "0%"
    ]
  ],
  "legend": [
    [
      "resolved",
      "unique static target"
    ],
    [
      "trait",
      "via trait/interface method"
    ],
    [
      "ambiguous",
      "n candidate targets"
    ],
    [
      "dynamic",
      "call site kept · target unknown"
    ],
    [
      "external",
      "outside the analysed source"
    ]
  ],
  "typesLabel": "classes in scope",
  "types": [
    "ActivityHeatStripSnapshot",
    "AllocCausalitySnapshot",
    "AllocationBurstDetector",
    "AllocationCausalityStat",
    "AllocationCollector",
    "AllocationSnapshot",
    "ArchivePerMod",
    "AttendanceSnapshot"
  ],
  "nodes": [
    {
      "id": "persistence_interactions_interactionnpc_cs_inter",
      "name": "InteractionNpc.OnSpawn()",
      "meta": "Persistence/Interactions/InteractionNpc.cs:30",
      "cert": "resolved",
      "row": 0,
      "sig": "OnSpawn(NPC npc, IEntitySource source)",
      "entry": true
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.OnHitNPC()",
      "meta": "Persistence/Interactions/InteractionPlayer.cs:124",
      "cert": "resolved",
      "row": 0,
      "sig": "OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)",
      "entry": true
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in_1",
      "name": "InteractionPlayer.OnHitNPCWithItem()",
      "meta": "Persistence/Interactions/InteractionPlayer.cs:144",
      "cert": "resolved",
      "row": 0,
      "sig": "OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone)",
      "entry": true
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_update_2",
      "name": "OverlayPanel.Update()",
      "meta": "UI/Overlay/OverlayPanel.cs:239",
      "cert": "resolved",
      "row": 0,
      "sig": "Update(GameTime gameTime)",
      "entry": true,
      "rec": true,
      "badge": "◇ ×7 sites"
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_posts",
      "name": "ProfilerSystem.PostSetupContent()",
      "meta": "Profiling/ProfilerSystem.cs:152",
      "cert": "resolved",
      "row": 0,
      "sig": "PostSetupContent()",
      "entry": true
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_postu",
      "name": "ProfilerSystem.PostUpdateEverything()",
      "meta": "Profiling/ProfilerSystem.cs:753",
      "cert": "resolved",
      "row": 0,
      "sig": "PostUpdateEverything()",
      "entry": true
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_clea",
      "name": "BiomeBitset.Clear()",
      "meta": "Profiling/Events/BiomeBitset.cs:63",
      "cert": "resolved",
      "row": 1,
      "sig": "Clear(int bit)",
      "badge": "◇ ×59 sites"
    },
    {
      "id": "data_collectors_contexttagger_cs_contexttagger_s",
      "name": "ContextTagger.Snapshot()",
      "meta": "Data/Collectors/ContextTagger.cs:62",
      "cert": "resolved",
      "row": 1,
      "sig": "Snapshot(long tickIndex)",
      "badge": "◇ ×3 sites"
    },
    {
      "id": "persistence_contexttransitionwatcher_cs_contextt",
      "name": "ContextTransitionWatcher.OnSnapshot()",
      "meta": "Persistence/ContextTransitionWatcher.cs:65",
      "cert": "resolved",
      "row": 1,
      "sig": "OnSnapshot(in EventContext ctx, double frameMs, SessionRecorder recorder)"
    },
    {
      "id": "insights_crosssession_crosssessiondetectors_cs_c",
      "name": "CostlyDespiteLowUsageDetector.Evaluate()",
      "meta": "Insights/CrossSession/CrossSessionDetectors.cs:109",
      "cert": "resolved",
      "row": 1,
      "sig": "Evaluate(CrossSessionInput input, List<Insight> emit)",
      "badge": "◇ ×5 sites"
    },
    {
      "id": "insights_crosssession_crosssessionevaluator_cs_c",
      "name": "CrossSessionEvaluator.Run()",
      "meta": "Insights/CrossSession/CrossSessionEvaluator.cs:32",
      "cert": "resolved",
      "row": 1,
      "sig": "Run(HistoryStore history, IReadOnlyList<string> roster, string fingerprint)",
      "badge": "◇ ×5 sites"
    },
    {
      "id": "persistence_interactions_interactionitem_cs_inte",
      "name": "InteractionItem.Emit()",
      "meta": "Persistence/Interactions/InteractionItem.cs:90",
      "cert": "resolved",
      "row": 1,
      "sig": "Emit(SessionRecorder recorder, Item item, string sourceContext, string contextCategor)",
      "badge": "◇ ×4 sites"
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_end",
      "name": "MetricCollector.EndTick()",
      "meta": "Profiling/MetricCollector.cs:559",
      "cert": "resolved",
      "row": 1,
      "sig": "EndTick(long tickIndex, int npcCount, int projectileCount, int dustCount)"
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_kicko",
      "name": "ProfilerSystem.KickOffSessionEndAsync()",
      "meta": "Profiling/ProfilerSystem.cs:499",
      "cert": "resolved",
      "row": 1,
      "sig": "KickOffSessionEndAsync()"
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_runde",
      "name": "ProfilerSystem.RunDeferredWorldLoadInit()",
      "meta": "Profiling/ProfilerSystem.cs:297",
      "cert": "resolved",
      "row": 1,
      "sig": "RunDeferredWorldLoadInit()"
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.Rent()",
      "meta": "Data/Aggregators/Segments/SegmentDetector.cs:477",
      "cert": "resolved",
      "row": 1,
      "sig": "Rent(int modCount)",
      "badge": "◇ ×10 sites"
    },
    {
      "id": "profiling_time_cs_time_unixmsnow_72",
      "name": "Time.UnixMsNow()",
      "meta": "Profiling/Time.cs:72",
      "cert": "resolved",
      "row": 1,
      "sig": "UnixMsNow()",
      "badge": "◇ ×31 sites"
    },
    {
      "id": "profiling_events_bossslotarray_cs_bossslotarray_",
      "name": "BossSlotArray.Contains()",
      "meta": "Profiling/Events/BossSlotArray.cs:83",
      "cert": "resolved",
      "row": 2,
      "sig": "Contains(short type)",
      "badge": "◇ ×13 sites"
    },
    {
      "id": "persistence_dbwriterthread_cs_dbwriterthread_enq",
      "name": "DbWriterThread.Enqueue()",
      "meta": "Persistence/DbWriterThread.cs:104",
      "cert": "resolved",
      "row": 2,
      "sig": "Enqueue(in DbWriteOp op)",
      "badge": "◇ ×19 sites"
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "name": "HeatmapAggregator.CurrentSnapshot()",
      "meta": "Data/Aggregators/HeatmapAggregator.cs:90",
      "cert": "resolved",
      "row": 2,
      "sig": "CurrentSnapshot()",
      "badge": "◇ ×30 sites"
    },
    {
      "id": "data_stats_kpicalculator_live_cs_kpicalculator_c",
      "name": "KpiCalculator.Compute()",
      "meta": "Data/Stats/KpiCalculator.Live.cs:20",
      "cert": "resolved",
      "row": 2,
      "sig": "Compute(MetricCollector? collector)",
      "badge": "◇ ×8 sites"
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_so",
      "name": "ModImpactScorer.Sort()",
      "meta": "Data/Stats/ModImpactScorer.cs:262",
      "cert": "resolved",
      "row": 2,
      "sig": "Sort(ImpactSortMode mode, bool descending)",
      "badge": "◇ ×33 sites"
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.Add()",
      "meta": "Data/Aggregators/PerModAttribution.cs:277",
      "cert": "resolved",
      "row": 2,
      "sig": "Add(int modId, int categoryId, int hookId, long elapsedStopwatchTicks)",
      "rec": true,
      "badge": "◇ ×179 sites"
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre_1",
      "name": "SessionRecorder.End()",
      "meta": "Persistence/Streams/SessionRecorder.cs:336",
      "cert": "resolved",
      "row": 2,
      "sig": "End(MetricCollector collector, string endReason = \\" \\", IReadOnlyList<double>? engage)"
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.DrainStalls()",
      "meta": "Persistence/Streams/SessionRecorder.cs:466",
      "cert": "resolved",
      "row": 3,
      "sig": "DrainStalls(MetricCollector collector)"
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre_2",
      "name": "SessionRecorder.ToList()",
      "meta": "Persistence/Streams/SessionRecorder.cs:824",
      "cert": "resolved",
      "row": 3,
      "sig": "ToList(float[] arr)",
      "badge": "◇ ×9 sites"
    },
    {
      "id": "ext_stopwatch_gettimestamp",
      "name": "Stopwatch.GetTimestamp",
      "meta": "external",
      "cert": "external",
      "ext": true,
      "row": 0,
      "doc": "Outside the analysed source; 68 call sites reach it."
    },
    {
      "id": "ext_jsonserializer_serialize",
      "name": "JsonSerializer.Serialize",
      "meta": "external",
      "cert": "external",
      "ext": true,
      "row": 0,
      "doc": "Outside the analysed source; 65 call sites reach it."
    },
    {
      "id": "ext_sb_append",
      "name": "sb.Append",
      "meta": "external",
      "cert": "external",
      "ext": true,
      "row": 0,
      "doc": "Outside the analysed source; 61 call sites reach it."
    }
  ],
  "edges": [
    [
      "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "data_aggregators_permodattribution_cs_permodattr",
      "ambiguous"
    ],
    [
      "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "profiling_time_cs_time_unixmsnow_72",
      "resolved"
    ],
    [
      "data_aggregators_permodattribution_cs_permodattr",
      "data_aggregators_permodattribution_cs_permodattr",
      "loop"
    ],
    [
      "insights_crosssession_crosssessiondetectors_cs_c",
      "data_aggregators_permodattribution_cs_permodattr",
      "ambiguous"
    ],
    [
      "insights_crosssession_crosssessiondetectors_cs_c",
      "data_stats_modimpactscorer_cs_modimpactscorer_so",
      "resolved"
    ],
    [
      "insights_crosssession_crosssessiondetectors_cs_c",
      "profiling_events_bossslotarray_cs_bossslotarray_",
      "ambiguous"
    ],
    [
      "insights_crosssession_crosssessionevaluator_cs_c",
      "data_aggregators_permodattribution_cs_permodattr",
      "ambiguous"
    ],
    [
      "insights_crosssession_crosssessionevaluator_cs_c",
      "insights_crosssession_crosssessiondetectors_cs_c",
      "ambiguous"
    ],
    [
      "persistence_interactions_interactionitem_cs_inte",
      "data_aggregators_segments_segmentdetector_cs_seg",
      "ambiguous"
    ],
    [
      "persistence_interactions_interactionitem_cs_inte",
      "profiling_time_cs_time_unixmsnow_72",
      "resolved"
    ],
    [
      "persistence_interactions_interactionnpc_cs_inter",
      "data_aggregators_segments_segmentdetector_cs_seg",
      "ambiguous"
    ],
    [
      "persistence_interactions_interactionnpc_cs_inter",
      "profiling_time_cs_time_unixmsnow_72",
      "resolved"
    ],
    [
      "persistence_interactions_interactionplayer_cs_in",
      "data_aggregators_segments_segmentdetector_cs_seg",
      "ambiguous"
    ],
    [
      "persistence_interactions_interactionplayer_cs_in",
      "profiling_time_cs_time_unixmsnow_72",
      "resolved"
    ],
    [
      "persistence_interactions_interactionplayer_cs_in_1",
      "data_aggregators_segments_segmentdetector_cs_seg",
      "ambiguous"
    ],
    [
      "persistence_interactions_interactionplayer_cs_in_1",
      "profiling_time_cs_time_unixmsnow_72",
      "resolved"
    ],
    [
      "persistence_streams_sessionrecorder_cs_sessionre",
      "persistence_dbwriterthread_cs_dbwriterthread_enq",
      "resolved"
    ],
    [
      "persistence_streams_sessionrecorder_cs_sessionre",
      "profiling_events_biomebitset_cs_biomebitset_clea",
      "ambiguous"
    ],
    [
      "persistence_streams_sessionrecorder_cs_sessionre_1",
      "persistence_dbwriterthread_cs_dbwriterthread_enq",
      "resolved"
    ],
    [
      "persistence_streams_sessionrecorder_cs_sessionre_1",
      "persistence_streams_sessionrecorder_cs_sessionre",
      "resolved"
    ],
    [
      "persistence_streams_sessionrecorder_cs_sessionre_2",
      "data_aggregators_permodattribution_cs_permodattr",
      "ambiguous"
    ],
    [
      "profiling_metriccollector_cs_metriccollector_end",
      "profiling_time_cs_time_unixmsnow_72",
      "resolved"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_kicko",
      "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_kicko",
      "data_aggregators_permodattribution_cs_permodattr",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_kicko",
      "data_stats_kpicalculator_live_cs_kpicalculator_c",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_kicko",
      "insights_crosssession_crosssessionevaluator_cs_c",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_kicko",
      "persistence_dbwriterthread_cs_dbwriterthread_enq",
      "resolved"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_kicko",
      "persistence_streams_sessionrecorder_cs_sessionre_1",
      "resolved"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_postu",
      "data_collectors_contexttagger_cs_contexttagger_s",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_postu",
      "insights_crosssession_crosssessiondetectors_cs_c",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_postu",
      "insights_crosssession_crosssessionevaluator_cs_c",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_postu",
      "persistence_contexttransitionwatcher_cs_contextt",
      "resolved"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_postu",
      "profiling_metriccollector_cs_metriccollector_end",
      "resolved"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_postu",
      "profiling_profilersystem_cs_profilersystem_runde",
      "resolved"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_postu",
      "profiling_time_cs_time_unixmsnow_72",
      "resolved"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_runde",
      "data_aggregators_permodattribution_cs_permodattr",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_runde",
      "data_stats_kpicalculator_live_cs_kpicalculator_c",
      "ambiguous"
    ],
    [
      "profiling_profilersystem_cs_profilersystem_runde",
      "insights_crosssession_crosssessionevaluator_cs_c",
      "ambiguous"
    ],
    [
      "ui_overlay_overlaypanel_cs_overlaypanel_update_2",
      "ui_overlay_overlaypanel_cs_overlaypanel_update_2",
      "loop"
    ]
  ],
  "tree": [
    {
      "pre": "",
      "tog": "▾",
      "name": "ProfilerSystem.PostUpdateEverything()",
      "meta": ":753",
      "hot": true
    },
    {
      "pre": "├─ ",
      "tog": "▾",
      "name": "EventAggregator.Accumulate()",
      "meta": ":114",
      "hot": true
    },
    {
      "pre": "│  ├─ ",
      "tog": "▾",
      "name": "EventAggregator.BumpBucket()",
      "meta": ":183"
    },
    {
      "pre": "│  │  └─ ",
      "tog": "◇",
      "name": "PerModAttribution.Add()",
      "meta": "×179 call sites",
      "multi": true
    },
    {
      "pre": "│  ├─ ",
      "tog": "↺",
      "name": "PerModAttribution.Add()",
      "meta": ":277",
      "note": "revisited - expansion blocked",
      "rec": true
    },
    {
      "pre": "│  ├─ ",
      "tog": "◇",
      "name": "BiomeBitset.Clear()",
      "meta": "×59 call sites",
      "multi": true
    },
    {
      "pre": "│  └─ ",
      "tog": "◇",
      "name": "BiomeBitset.IsSet()",
      "meta": "×3 call sites",
      "multi": true
    },
    {
      "pre": "├─ ",
      "tog": "▾",
      "name": "PerModCostTimeSeriesAggregator.OnTick()",
      "meta": ":147"
    },
    {
      "pre": "│  └─ ",
      "tog": "▾",
      "name": "PerModCostTimeSeriesAggregator.CloseBucket()",
      "meta": ":199"
    },
    {
      "pre": "│     └─ ",
      "tog": "↺",
      "name": "BiomeBitset.Clear()",
      "meta": ":63",
      "note": "revisited - expansion blocked",
      "rec": true
    },
    {
      "pre": "├─ ",
      "tog": "▾",
      "name": "SegmentDetector.OnDeath()",
      "meta": ":289"
    },
    {
      "pre": "│  ├─ ",
      "tog": "◇",
      "name": "SegmentDetector.CloseAndPublish()",
      "meta": "×5 call sites",
      "multi": true,
      "hot": true
    },
    {
      "pre": "│  │  ├─ ",
      "tog": "▸",
      "name": "SegmentDetector.BuildSegment()",
      "meta": ":429"
    },
    {
      "pre": "│  │  ├─ ",
      "tog": "◇",
      "name": "SegmentDetector.Return()",
      "meta": "×7 call sites",
      "multi": true
    },
    {
      "pre": "│  │  ├─ ",
      "tog": "◇",
      "name": "SegmentNameTable.For()",
      "meta": "×3 call sites",
      "multi": true
    },
    {
      "pre": "│  │  └─ ",
      "tog": "◇",
      "name": "BoolIndex.Remove()",
      "meta": "×5 call sites",
      "multi": true
    }
  ],
  "fns": [
    {
      "id": "data_aggregators_eventaggregator_cs_eventbucketr",
      "name": "EventBucketRow()",
      "file": "Data/Aggregators/EventAggregator.cs",
      "line": 45,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_eventaggregator_cs_eventaggrega",
      "name": "EventAggregator()",
      "file": "Data/Aggregators/EventAggregator.cs",
      "line": 81,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_eventaggregator_cs_eventaggrega",
      "name": "EventAggregator.Accumulate",
      "file": "Data/Aggregators/EventAggregator.cs",
      "line": 114,
      "in": 1,
      "out": 4
    },
    {
      "id": "data_aggregators_eventaggregator_cs_eventaggrega",
      "name": "EventAggregator.BumpBucket",
      "file": "Data/Aggregators/EventAggregator.cs",
      "line": 183,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_eventaggregator_cs_eventaggrega",
      "name": "EventAggregator.IsActiveNow",
      "file": "Data/Aggregators/EventAggregator.cs",
      "line": 195,
      "in": 2,
      "out": 1
    },
    {
      "id": "data_aggregators_eventaggregator_cs_eventaggrega",
      "name": "EventAggregator.SnapshotRows",
      "file": "Data/Aggregators/EventAggregator.cs",
      "line": 209,
      "in": 1,
      "out": 6
    },
    {
      "id": "data_aggregators_eventaggregator_cs_eventaggrega",
      "name": "EventAggregator.InvasionDisplay",
      "file": "Data/Aggregators/EventAggregator.cs",
      "line": 246,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_eventaggregator_cs_eventaggrega",
      "name": "EventAggregator.DifficultyDisplay",
      "file": "Data/Aggregators/EventAggregator.cs",
      "line": 256,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapbos",
      "name": "HeatmapBossOverlay()",
      "file": "Data/Aggregators/HeatmapAggregator.cs",
      "line": 31,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapsna",
      "name": "HeatmapSnapshot()",
      "file": "Data/Aggregators/HeatmapAggregator.cs",
      "line": 45,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "name": "HeatmapAggregator.Initialise",
      "file": "Data/Aggregators/HeatmapAggregator.cs",
      "line": 86,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "name": "HeatmapAggregator.Reset",
      "file": "Data/Aggregators/HeatmapAggregator.cs",
      "line": 87,
      "in": 4,
      "out": 0
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "name": "HeatmapAggregator.Dispose",
      "file": "Data/Aggregators/HeatmapAggregator.cs",
      "line": 88,
      "in": 7,
      "out": 0
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "name": "HeatmapAggregator.CurrentSnapshot",
      "file": "Data/Aggregators/HeatmapAggregator.cs",
      "line": 90,
      "in": 30,
      "out": 6
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "name": "HeatmapAggregator.BucketFromDb",
      "file": "Data/Aggregators/HeatmapAggregator.cs",
      "line": 124,
      "in": 1,
      "out": 4
    },
    {
      "id": "data_aggregators_heatmapaggregator_cs_heatmapagg",
      "name": "HeatmapAggregator.BucketFromMemory",
      "file": "Data/Aggregators/HeatmapAggregator.cs",
      "line": 187,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_aggregators_heatmapfold_cs_heatmapbucket_ct",
      "name": "HeatmapBucket()",
      "file": "Data/Aggregators/HeatmapFold.cs",
      "line": 16,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_aggregators_heatmapfold_cs_heatmapfold_fold",
      "name": "HeatmapFold.Fold",
      "file": "Data/Aggregators/HeatmapFold.cs",
      "line": 46,
      "in": 2,
      "out": 2
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.Initialise",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 51,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.Reset",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 52,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.Dispose",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 53,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.CurrentSnapshot",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 55,
      "in": 0,
      "out": 10
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.SumStallContribs",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 196,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.MakeFingerprint",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 207,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.Accumulate",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 211,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.AccumulateCell",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 236,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_lagfingerprintaggregator_cs_lag",
      "name": "LagFingerprintAggregator.Percentile",
      "file": "Data/Aggregators/LagFingerprintAggregator.cs",
      "line": 249,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_lagrhythmaggregator_cs_lagrhyth",
      "name": "LagRhythmAggregator.Initialise",
      "file": "Data/Aggregators/LagRhythmAggregator.cs",
      "line": 47,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_lagrhythmaggregator_cs_lagrhyth",
      "name": "LagRhythmAggregator.Reset",
      "file": "Data/Aggregators/LagRhythmAggregator.cs",
      "line": 48,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_lagrhythmaggregator_cs_lagrhyth",
      "name": "LagRhythmAggregator.Dispose",
      "file": "Data/Aggregators/LagRhythmAggregator.cs",
      "line": 49,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_lagrhythmaggregator_cs_lagrhyth",
      "name": "LagRhythmAggregator.CurrentSnapshot",
      "file": "Data/Aggregators/LagRhythmAggregator.cs",
      "line": 51,
      "in": 0,
      "out": 6
    },
    {
      "id": "data_aggregators_lagrhythmaggregator_cs_lagrhyth",
      "name": "LagRhythmAggregator.BucketCentreSeconds",
      "file": "Data/Aggregators/LagRhythmAggregator.cs",
      "line": 203,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_lagrhythmaggregator_cs_lagrhyth",
      "name": "LagRhythmAggregator.TopModFromSpike",
      "file": "Data/Aggregators/LagRhythmAggregator.cs",
      "line": 209,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_aggregators_lagrhythmaggregator_cs_event_ct",
      "name": "Event()",
      "file": "Data/Aggregators/LagRhythmAggregator.cs",
      "line": 230,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.Configure",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 127,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.Configure",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 133,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.Configure",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 143,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.Configure",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 151,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.RegisterHook",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 210,
      "in": 2,
      "out": 2
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.RegisterOrReuseHook",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 235,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.BeginTick",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 251,
      "in": 2,
      "out": 1
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.Add",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 277,
      "in": 179,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.Add",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 290,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.Add",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 342,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.HarvestDrawInto",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 398,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.HarvestInto",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 419,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.HarvestInto",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 430,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.HarvestHooksInto",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 450,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.HarvestHooksInto",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 459,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.HarvestAllocationsInto",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 481,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_permodattribution_cs_permodattr",
      "name": "PerModAttribution.HarvestHookAllocationsInto",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 502,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_permodattribution_cs_hookdescri",
      "name": "HookDescriptor()",
      "file": "Data/Aggregators/PerModAttribution.cs",
      "line": 526,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodcosttimeseriesaggregator_",
      "name": "PerModCostTimeSeriesAggregator()",
      "file": "Data/Aggregators/PerModCostTimeSeriesAggregator.cs",
      "line": 90,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodcosttimeseriesaggregator_",
      "name": "PerModCostTimeSeriesAggregator()",
      "file": "Data/Aggregators/PerModCostTimeSeriesAggregator.cs",
      "line": 92,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodcosttimeseriesaggregator_",
      "name": "PerModCostTimeSeriesAggregator.Initialise",
      "file": "Data/Aggregators/PerModCostTimeSeriesAggregator.cs",
      "line": 99,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodcosttimeseriesaggregator_",
      "name": "PerModCostTimeSeriesAggregator.Reset",
      "file": "Data/Aggregators/PerModCostTimeSeriesAggregator.cs",
      "line": 118,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodcosttimeseriesaggregator_",
      "name": "PerModCostTimeSeriesAggregator.Dispose",
      "file": "Data/Aggregators/PerModCostTimeSeriesAggregator.cs",
      "line": 140,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodcosttimeseriesaggregator_",
      "name": "PerModCostTimeSeriesAggregator.OnTick",
      "file": "Data/Aggregators/PerModCostTimeSeriesAggregator.cs",
      "line": 147,
      "in": 2,
      "out": 1
    },
    {
      "id": "data_aggregators_permodcosttimeseriesaggregator_",
      "name": "PerModCostTimeSeriesAggregator.CloseBucket",
      "file": "Data/Aggregators/PerModCostTimeSeriesAggregator.cs",
      "line": 199,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_permodcosttimeseriesaggregator_",
      "name": "PerModCostTimeSeriesAggregator.CurrentSnapshot",
      "file": "Data/Aggregators/PerModCostTimeSeriesAggregator.cs",
      "line": 215,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodsample_cs_permodsample_re",
      "name": "PerModSample.ResetMeasurements",
      "file": "Data/Aggregators/PerModSample.cs",
      "line": 46,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.Initialise",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 90,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.Reset",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 110,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.Dispose",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 120,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.ModIdForName",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 125,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.IncrementItemCreated",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 139,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.IncrementNpcSpawned",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 145,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.IncrementNpcKilled",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 151,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.IncrementBuffApplied",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 157,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.Capture",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 165,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.CaptureInstance",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 172,
      "in": 1,
      "out": 6
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.CurrentSnapshot",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 289,
      "in": 0,
      "out": 3
    },
    {
      "id": "data_aggregators_permodusageaggregator_cs_permod",
      "name": "PerModUsageAggregator.IsNonEmpty",
      "file": "Data/Aggregators/PerModUsageAggregator.cs",
      "line": 328,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_pertickattributionring_cs_perti",
      "name": "PerTickAttributionRing()",
      "file": "Data/Aggregators/PerTickAttributionRing.cs",
      "line": 97,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_pertickattributionring_cs_perti",
      "name": "PerTickAttributionRing.RoundUpPow2",
      "file": "Data/Aggregators/PerTickAttributionRing.cs",
      "line": 120,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_pertickattributionring_cs_perti",
      "name": "PerTickAttributionRing.Push",
      "file": "Data/Aggregators/PerTickAttributionRing.cs",
      "line": 144,
      "in": 7,
      "out": 0
    },
    {
      "id": "data_aggregators_pertickattributionring_cs_perti",
      "name": "PerTickAttributionRing.GetPerModMs",
      "file": "Data/Aggregators/PerTickAttributionRing.cs",
      "line": 193,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_pertickattributionring_cs_perti",
      "name": "PerTickAttributionRing.GetPerModBytes",
      "file": "Data/Aggregators/PerTickAttributionRing.cs",
      "line": 209,
      "in": 3,
      "out": 0
    },
    {
      "id": "data_aggregators_pertickattributionring_cs_perti",
      "name": "PerTickAttributionRing.CopyLatestCategorySnapshot",
      "file": "Data/Aggregators/PerTickAttributionRing.cs",
      "line": 227,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_pertickattributionring_cs_perti",
      "name": "PerTickAttributionRing.TryGetCategorySnapshot",
      "file": "Data/Aggregators/PerTickAttributionRing.cs",
      "line": 265,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segmentaggregator_cs_segmentssn",
      "name": "SegmentsSnapshot()",
      "file": "Data/Aggregators/SegmentAggregator.cs",
      "line": 28,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segmentaggregator_cs_segmentagg",
      "name": "SegmentAggregator.Initialise",
      "file": "Data/Aggregators/SegmentAggregator.cs",
      "line": 65,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segmentaggregator_cs_segmentagg",
      "name": "SegmentAggregator.Reset",
      "file": "Data/Aggregators/SegmentAggregator.cs",
      "line": 66,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segmentaggregator_cs_segmentagg",
      "name": "SegmentAggregator.Dispose",
      "file": "Data/Aggregators/SegmentAggregator.cs",
      "line": 67,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segmentaggregator_cs_segmentagg",
      "name": "SegmentAggregator.CurrentSnapshot",
      "file": "Data/Aggregators/SegmentAggregator.cs",
      "line": 69,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_segments_opensegment_cs_openseg",
      "name": "OpenSegment.Reset",
      "file": "Data/Aggregators/Segments/OpenSegment.cs",
      "line": 58,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_segments_segment_cs_segment_fro",
      "name": "Segment.FromRow",
      "file": "Data/Aggregators/Segments/Segment.cs",
      "line": 74,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector()",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 101,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.OnTick",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 123,
      "in": 0,
      "out": 9
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.OnSpike",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 274,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.OnStall",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 280,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.OnDeath",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 289,
      "in": 1,
      "out": 3
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.OnCombatHit",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 304,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.OpenBookmark",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 316,
      "in": 0,
      "out": 2
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.CloseBookmark",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 331,
      "in": 0,
      "out": 2
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.CloseAllOnShutdown",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 340,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.SweepOpen",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 359,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.SweepClose",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 367,
      "in": 1,
      "out": 5
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.OpenIfAbsent",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 388,
      "in": 4,
      "out": 3
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.CloseAndPublish",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 405,
      "in": 5,
      "out": 4
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.BuildSegment",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 429,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.Rent",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 477,
      "in": 10,
      "out": 1
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.Return",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 484,
      "in": 7,
      "out": 1
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.Compose",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 490,
      "in": 6,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentdetector_cs_seg",
      "name": "SegmentDetector.ComputeBiomeComposite",
      "file": "Data/Aggregators/Segments/SegmentDetector.cs",
      "line": 503,
      "in": 1,
      "out": 4
    },
    {
      "id": "data_aggregators_segments_segmentlifetimestat_cs",
      "name": "SegmentLifetimeStat.Initialise",
      "file": "Data/Aggregators/Segments/SegmentLifetimeStat.cs",
      "line": 34,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentlifetimestat_cs",
      "name": "SegmentLifetimeStat.Reset",
      "file": "Data/Aggregators/Segments/SegmentLifetimeStat.cs",
      "line": 35,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentlifetimestat_cs",
      "name": "SegmentLifetimeStat.Dispose",
      "file": "Data/Aggregators/Segments/SegmentLifetimeStat.cs",
      "line": 36,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentlifetimestat_cs",
      "name": "SegmentLifetimeStat.CurrentSnapshot",
      "file": "Data/Aggregators/Segments/SegmentLifetimeStat.cs",
      "line": 38,
      "in": 0,
      "out": 4
    },
    {
      "id": "data_aggregators_segments_segmentmodattributions",
      "name": "SegmentModAttributionStat.Initialise",
      "file": "Data/Aggregators/Segments/SegmentModAttributionStat.cs",
      "line": 37,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentmodattributions",
      "name": "SegmentModAttributionStat.Reset",
      "file": "Data/Aggregators/Segments/SegmentModAttributionStat.cs",
      "line": 38,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentmodattributions",
      "name": "SegmentModAttributionStat.Dispose",
      "file": "Data/Aggregators/Segments/SegmentModAttributionStat.cs",
      "line": 39,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentmodattributions",
      "name": "SegmentModAttributionStat.CurrentSnapshot",
      "file": "Data/Aggregators/Segments/SegmentModAttributionStat.cs",
      "line": 41,
      "in": 0,
      "out": 2
    },
    {
      "id": "data_aggregators_segments_segmentnametable_cs_se",
      "name": "SegmentNameTable.For",
      "file": "Data/Aggregators/Segments/SegmentNameTable.cs",
      "line": 32,
      "in": 3,
      "out": 3
    },
    {
      "id": "data_aggregators_segments_segmentnametable_cs_se",
      "name": "SegmentNameTable.BiomeName",
      "file": "Data/Aggregators/Segments/SegmentNameTable.cs",
      "line": 46,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentnametable_cs_se",
      "name": "SegmentNameTable.InvasionName",
      "file": "Data/Aggregators/Segments/SegmentNameTable.cs",
      "line": 56,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentpromoter_cs_pro",
      "name": "PromotionResult()",
      "file": "Data/Aggregators/Segments/SegmentPromoter.cs",
      "line": 33,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentpromoter_cs_seg",
      "name": "SegmentPromoter.Decide",
      "file": "Data/Aggregators/Segments/SegmentPromoter.cs",
      "line": 51,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_segments_segmentstore_cs_segmen",
      "name": "SegmentStore()",
      "file": "Data/Aggregators/Segments/SegmentStore.cs",
      "line": 53,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_aggregators_segments_segmentstore_cs_segmen",
      "name": "SegmentStore.TryDequeueToast",
      "file": "Data/Aggregators/Segments/SegmentStore.cs",
      "line": 67,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentstore_cs_segmen",
      "name": "SegmentStore.LifetimeAvgMsPerTick",
      "file": "Data/Aggregators/Segments/SegmentStore.cs",
      "line": 75,
      "in": 4,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentstore_cs_segmen",
      "name": "SegmentStore.LifetimeSampleCount",
      "file": "Data/Aggregators/Segments/SegmentStore.cs",
      "line": 82,
      "in": 4,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentstore_cs_segmen",
      "name": "SegmentStore.FoldLifetime",
      "file": "Data/Aggregators/Segments/SegmentStore.cs",
      "line": 123,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentstore_cs_segmen",
      "name": "SegmentStore.SeedLifetimeFromDb",
      "file": "Data/Aggregators/Segments/SegmentStore.cs",
      "line": 136,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_aggregators_segments_segmentstore_cs_segmen",
      "name": "SegmentStore.ToRow",
      "file": "Data/Aggregators/Segments/SegmentStore.cs",
      "line": 157,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_aggregators_sessionactivityheatstripaggrega",
      "name": "SessionActivityHeatStripAggregator.Initialise",
      "file": "Data/Aggregators/SessionActivityHeatStripAggregator.cs",
      "line": 42,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_sessionactivityheatstripaggrega",
      "name": "SessionActivityHeatStripAggregator.Reset",
      "file": "Data/Aggregators/SessionActivityHeatStripAggregator.cs",
      "line": 43,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_sessionactivityheatstripaggrega",
      "name": "SessionActivityHeatStripAggregator.Dispose",
      "file": "Data/Aggregators/SessionActivityHeatStripAggregator.cs",
      "line": 44,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_aggregators_sessionactivityheatstripaggrega",
      "name": "SessionActivityHeatStripAggregator.CurrentSnapshot",
      "file": "Data/Aggregators/SessionActivityHeatStripAggregator.cs",
      "line": 46,
      "in": 0,
      "out": 4
    },
    {
      "id": "data_aggregators_sessionactivityheatstripaggrega",
      "name": "SessionActivityHeatStripAggregator.Get",
      "file": "Data/Aggregators/SessionActivityHeatStripAggregator.cs",
      "line": 146,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_collectors_allocationcollector_cs_allocatio",
      "name": "AllocationSnapshot()",
      "file": "Data/Collectors/AllocationCollector.cs",
      "line": 33,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_collectors_allocationcollector_cs_allocatio",
      "name": "AllocationCollector.Initialise",
      "file": "Data/Collectors/AllocationCollector.cs",
      "line": 69,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_allocationcollector_cs_allocatio",
      "name": "AllocationCollector.Reset",
      "file": "Data/Collectors/AllocationCollector.cs",
      "line": 70,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_allocationcollector_cs_allocatio",
      "name": "AllocationCollector.Dispose",
      "file": "Data/Collectors/AllocationCollector.cs",
      "line": 71,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_allocationcollector_cs_allocatio",
      "name": "AllocationCollector.CurrentSnapshot",
      "file": "Data/Collectors/AllocationCollector.cs",
      "line": 73,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_collectors_contexttagger_cs_contexttagger_r",
      "name": "ContextTagger.Reset",
      "file": "Data/Collectors/ContextTagger.cs",
      "line": 47,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_collectors_contexttagger_cs_contexttagger_s",
      "name": "ContextTagger.Snapshot",
      "file": "Data/Collectors/ContextTagger.cs",
      "line": 62,
      "in": 3,
      "out": 5
    },
    {
      "id": "data_collectors_contexttagger_cs_contexttagger_s",
      "name": "ContextTagger.SampleWeather",
      "file": "Data/Collectors/ContextTagger.cs",
      "line": 99,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_collectors_contexttagger_cs_contexttagger_s",
      "name": "ContextTagger.SampleBiomes",
      "file": "Data/Collectors/ContextTagger.cs",
      "line": 110,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_collectors_contexttagger_cs_contexttagger_s",
      "name": "ContextTagger.SampleInvasion",
      "file": "Data/Collectors/ContextTagger.cs",
      "line": 121,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_collectors_contexttagger_cs_contexttagger_s",
      "name": "ContextTagger.SampleSlow",
      "file": "Data/Collectors/ContextTagger.cs",
      "line": 142,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_collectors_frametimecollector_cs_frametimes",
      "name": "FrameTimeSnapshot()",
      "file": "Data/Collectors/FrameTimeCollector.cs",
      "line": 32,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_collectors_frametimecollector_cs_frametimec",
      "name": "FrameTimeCollector.Initialise",
      "file": "Data/Collectors/FrameTimeCollector.cs",
      "line": 81,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_frametimecollector_cs_frametimec",
      "name": "FrameTimeCollector.Reset",
      "file": "Data/Collectors/FrameTimeCollector.cs",
      "line": 82,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_frametimecollector_cs_frametimec",
      "name": "FrameTimeCollector.Dispose",
      "file": "Data/Collectors/FrameTimeCollector.cs",
      "line": 83,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_frametimecollector_cs_frametimec",
      "name": "FrameTimeCollector.CurrentSnapshot",
      "file": "Data/Collectors/FrameTimeCollector.cs",
      "line": 85,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_collectors_hookcpucollector_cs_hookcpusnaps",
      "name": "HookCpuSnapshot()",
      "file": "Data/Collectors/HookCpuCollector.cs",
      "line": 40,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_collectors_hookcpucollector_cs_hookcpucolle",
      "name": "HookCpuCollector.Initialise",
      "file": "Data/Collectors/HookCpuCollector.cs",
      "line": 76,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_hookcpucollector_cs_hookcpucolle",
      "name": "HookCpuCollector.Reset",
      "file": "Data/Collectors/HookCpuCollector.cs",
      "line": 77,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_hookcpucollector_cs_hookcpucolle",
      "name": "HookCpuCollector.Dispose",
      "file": "Data/Collectors/HookCpuCollector.cs",
      "line": 78,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_hookcpucollector_cs_hookcpucolle",
      "name": "HookCpuCollector.CurrentSnapshot",
      "file": "Data/Collectors/HookCpuCollector.cs",
      "line": 80,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_collectors_modrosterscanner_cs_modrostersca",
      "name": "ModRosterScanner.Initialise",
      "file": "Data/Collectors/ModRosterScanner.cs",
      "line": 62,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_modrosterscanner_cs_modrostersca",
      "name": "ModRosterScanner.Reset",
      "file": "Data/Collectors/ModRosterScanner.cs",
      "line": 63,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_modrosterscanner_cs_modrostersca",
      "name": "ModRosterScanner.Dispose",
      "file": "Data/Collectors/ModRosterScanner.cs",
      "line": 64,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_modrosterscanner_cs_modrostersca",
      "name": "ModRosterScanner.CurrentSnapshot",
      "file": "Data/Collectors/ModRosterScanner.cs",
      "line": 66,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_modrosterscanner_cs_modrostersca",
      "name": "ModRosterScanner.CurrentSnapshotBoxed",
      "file": "Data/Collectors/ModRosterScanner.cs",
      "line": 67,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_collectors_modrosterscanner_cs_modrostersca",
      "name": "ModRosterScanner.Scan",
      "file": "Data/Collectors/ModRosterScanner.cs",
      "line": 75,
      "in": 1,
      "out": 3
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_modrostersnap",
      "name": "ModRosterSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 53,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_modusagesnaps",
      "name": "ModUsageSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 90,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_modcosttimese",
      "name": "ModCostTimeSeriesSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 106,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_segmentlifeti",
      "name": "SegmentLifetimeSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 130,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_segmentmodatt",
      "name": "SegmentModAttributionSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 147,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_transitiontra",
      "name": "TransitionTrackSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 165,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_activityheats",
      "name": "ActivityHeatStripSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 183,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_attendancesna",
      "name": "AttendanceSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 204,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_deathreplaysn",
      "name": "DeathReplaySnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 235,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_sessionchroni",
      "name": "SessionChronicleSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 251,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_lagclustersna",
      "name": "LagClusterSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 285,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_gcpressuresna",
      "name": "GcPressureSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 306,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_segmentlagden",
      "name": "SegmentLagDensitySnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 336,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_alloccausalit",
      "name": "AllocCausalitySnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 364,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_lagrhythmsnap",
      "name": "LagRhythmSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 388,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_modobservator",
      "name": "ModObservatorySnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 432,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_dormantsurfac",
      "name": "DormantSurfaceSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 455,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_crosscuttings",
      "name": "CrossCuttingSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 479,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_engagementcos",
      "name": "EngagementCostSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 496,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_contracts_rolloutcontracts_cs_modinteractio",
      "name": "ModInteractionSnapshot()",
      "file": "Data/Contracts/RolloutContracts.cs",
      "line": 517,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_dataregistry_cs_dataregistry_register_64",
      "name": "DataRegistry.Register",
      "file": "Data/DataRegistry.cs",
      "line": 64,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_dataregistry_cs_dataregistry_lookup_78",
      "name": "DataRegistry.Lookup",
      "file": "Data/DataRegistry.cs",
      "line": 78,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_dataregistry_cs_dataregistry_lookup_82",
      "name": "DataRegistry.Lookup",
      "file": "Data/DataRegistry.cs",
      "line": 82,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_dataregistry_cs_dataregistry_freeze_96",
      "name": "DataRegistry.Freeze",
      "file": "Data/DataRegistry.cs",
      "line": 96,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_dataregistry_cs_dataregistry_initialiseall_",
      "name": "DataRegistry.InitialiseAll",
      "file": "Data/DataRegistry.cs",
      "line": 115,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_dataregistry_cs_dataregistry_resetall_122",
      "name": "DataRegistry.ResetAll",
      "file": "Data/DataRegistry.cs",
      "line": 122,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_dataregistry_cs_dataregistry_disposeall_133",
      "name": "DataRegistry.DisposeAll",
      "file": "Data/DataRegistry.cs",
      "line": 133,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_detectors_spikedetector_cs_spikedetector_ct",
      "name": "SpikeDetector()",
      "file": "Data/Detectors/SpikeDetector.cs",
      "line": 131,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_detectors_spikedetector_cs_spikedetector_on",
      "name": "SpikeDetector.OnTick",
      "file": "Data/Detectors/SpikeDetector.cs",
      "line": 156,
      "in": 0,
      "out": 2
    },
    {
      "id": "data_detectors_spikedetector_cs_spikedetector_ha",
      "name": "SpikeDetector.HandleSubThreshold",
      "file": "Data/Detectors/SpikeDetector.cs",
      "line": 228,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_detectors_spikedetector_cs_spikedetector_fl",
      "name": "SpikeDetector.Flush",
      "file": "Data/Detectors/SpikeDetector.cs",
      "line": 244,
      "in": 4,
      "out": 1
    },
    {
      "id": "data_detectors_spikedetector_cs_spikedetector_ca",
      "name": "SpikeDetector.CaptureSnapshot",
      "file": "Data/Detectors/SpikeDetector.cs",
      "line": 254,
      "in": 2,
      "out": 1
    },
    {
      "id": "data_detectors_spikedetector_cs_spikewindowsview",
      "name": "SpikeWindowsView()",
      "file": "Data/Detectors/SpikeDetector.cs",
      "line": 276,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_detectors_spikedetector_cs_spikewindowsview",
      "name": "SpikeWindowsView.GetEnumerator",
      "file": "Data/Detectors/SpikeDetector.cs",
      "line": 280,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_ct",
      "name": "StallDetector()",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 244,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_on",
      "name": "StallDetector.OnBeginTick",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 282,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_on",
      "name": "StallDetector.OnBeginTick",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 285,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_on",
      "name": "StallDetector.OnBeginTick",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 295,
      "in": 0,
      "out": 8
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_co",
      "name": "StallDetector.CountRecentStallsInWindow",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 406,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_ca",
      "name": "StallDetector.CaptureTopContributors",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 421,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_cl",
      "name": "StallDetector.ClassifyCause",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 458,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_cl",
      "name": "StallDetector.ClassifyCause",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 475,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_cl",
      "name": "StallDetector.ClassifyCause",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 479,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_cl",
      "name": "StallDetector.ClassifyCause",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 499,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_cl",
      "name": "StallDetector.ClassifySeverity",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 558,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_cl",
      "name": "StallDetector.ClassifySeverity",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 569,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_re",
      "name": "StallDetector.Reset",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 573,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_ca",
      "name": "StallDetector.CaptureBaseline",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 587,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_detectors_stalldetector_cs_stalldetector_sa",
      "name": "StallDetector.SafeGcPauseMs",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 599,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalleventsview_",
      "name": "StallEventsView()",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 612,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_detectors_stalldetector_cs_stalleventsview_",
      "name": "StallEventsView.GetEnumerator",
      "file": "Data/Detectors/StallDetector.cs",
      "line": 616,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_allocationcausalitystat_cs_allocation",
      "name": "AllocationCausalityStat.Initialise",
      "file": "Data/Stats/AllocationCausalityStat.cs",
      "line": 44,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_allocationcausalitystat_cs_allocation",
      "name": "AllocationCausalityStat.Reset",
      "file": "Data/Stats/AllocationCausalityStat.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_allocationcausalitystat_cs_allocation",
      "name": "AllocationCausalityStat.Dispose",
      "file": "Data/Stats/AllocationCausalityStat.cs",
      "line": 46,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_allocationcausalitystat_cs_allocation",
      "name": "AllocationCausalityStat.CurrentSnapshot",
      "file": "Data/Stats/AllocationCausalityStat.cs",
      "line": 48,
      "in": 0,
      "out": 6
    },
    {
      "id": "data_stats_baseline_cs_baseline_recompute_138",
      "name": "Baseline.Recompute",
      "file": "Data/Stats/Baseline.cs",
      "line": 138,
      "in": 2,
      "out": 1
    },
    {
      "id": "data_stats_baseline_cs_baseline_recompute_152",
      "name": "Baseline.Recompute",
      "file": "Data/Stats/Baseline.cs",
      "line": 152,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_stats_baseline_cs_baseline_recomputecore_15",
      "name": "Baseline.RecomputeCore",
      "file": "Data/Stats/Baseline.cs",
      "line": 157,
      "in": 2,
      "out": 4
    },
    {
      "id": "data_stats_baseline_cs_baseline_onframepushed_24",
      "name": "Baseline.OnFramePushed",
      "file": "Data/Stats/Baseline.cs",
      "line": 241,
      "in": 2,
      "out": 1
    },
    {
      "id": "data_stats_baseline_cs_baseline_reset_282",
      "name": "Baseline.Reset",
      "file": "Data/Stats/Baseline.cs",
      "line": 282,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_stats_baseline_cs_baseline_rebuildfromhisto",
      "name": "Baseline.RebuildFromHistory",
      "file": "Data/Stats/Baseline.cs",
      "line": 308,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_stats_baseline_cs_baseline_bucketfor_326",
      "name": "Baseline.BucketFor",
      "file": "Data/Stats/Baseline.cs",
      "line": 326,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_stats_baseline_cs_baseline_histogrammedian_",
      "name": "Baseline.HistogramMedian",
      "file": "Data/Stats/Baseline.cs",
      "line": 339,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_stats_baseline_cs_baseline_computemadfromhi",
      "name": "Baseline.ComputeMadFromHistory",
      "file": "Data/Stats/Baseline.cs",
      "line": 357,
      "in": 1,
      "out": 3
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_in",
      "name": "DeathReplayStat.Initialise",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 51,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_re",
      "name": "DeathReplayStat.Reset",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 52,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_di",
      "name": "DeathReplayStat.Dispose",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 53,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_cu",
      "name": "DeathReplayStat.CurrentSnapshot",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 55,
      "in": 0,
      "out": 10
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_re",
      "name": "DeathReplayStat.ResolveModId",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 212,
      "in": 2,
      "out": 1
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_re",
      "name": "DeathReplayStat.ResolveModIdFromType",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 227,
      "in": 3,
      "out": 5
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_re",
      "name": "DeathReplayStat.ResolveDamageModId",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 244,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_re",
      "name": "DeathReplayStat.ResolveDamageModIdFromContributor",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 255,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_stats_deathreplaystat_cs_deathreplaystat_re",
      "name": "DeathReplayStat.ResolvePrimaryBiome",
      "file": "Data/Stats/DeathReplayStat.cs",
      "line": 274,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_eventsfeed_cs_eventsfeed_build_51",
      "name": "EventsFeed.Build",
      "file": "Data/Stats/EventsFeed.cs",
      "line": 51,
      "in": 1,
      "out": 3
    },
    {
      "id": "data_stats_eventsfeed_cs_eventsfeed_formatdurati",
      "name": "EventsFeed.FormatDuration",
      "file": "Data/Stats/EventsFeed.cs",
      "line": 130,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_stats_eventsfeedstat_cs_eventsfeedsnapshot_",
      "name": "EventsFeedSnapshot()",
      "file": "Data/Stats/EventsFeedStat.cs",
      "line": 28,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_eventsfeedstat_cs_eventsfeedstat_init",
      "name": "EventsFeedStat.Initialise",
      "file": "Data/Stats/EventsFeedStat.cs",
      "line": 57,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_eventsfeedstat_cs_eventsfeedstat_rese",
      "name": "EventsFeedStat.Reset",
      "file": "Data/Stats/EventsFeedStat.cs",
      "line": 58,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_eventsfeedstat_cs_eventsfeedstat_disp",
      "name": "EventsFeedStat.Dispose",
      "file": "Data/Stats/EventsFeedStat.cs",
      "line": 59,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_eventsfeedstat_cs_eventsfeedstat_curr",
      "name": "EventsFeedStat.CurrentSnapshot",
      "file": "Data/Stats/EventsFeedStat.cs",
      "line": 61,
      "in": 0,
      "out": 2
    },
    {
      "id": "data_stats_gcpressurestat_cs_gcpressurestat_init",
      "name": "GcPressureStat.Initialise",
      "file": "Data/Stats/GcPressureStat.cs",
      "line": 46,
      "in": 0,
      "out": 2
    },
    {
      "id": "data_stats_gcpressurestat_cs_gcpressurestat_rese",
      "name": "GcPressureStat.Reset",
      "file": "Data/Stats/GcPressureStat.cs",
      "line": 52,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_stats_gcpressurestat_cs_gcpressurestat_disp",
      "name": "GcPressureStat.Dispose",
      "file": "Data/Stats/GcPressureStat.cs",
      "line": 58,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_gcpressurestat_cs_gcpressurestat_curr",
      "name": "GcPressureStat.CurrentSnapshot",
      "file": "Data/Stats/GcPressureStat.cs",
      "line": 60,
      "in": 0,
      "out": 3
    },
    {
      "id": "data_stats_hookcoverageview_cs_hookcoverageview_",
      "name": "HookCoverageView.TotalHooks",
      "file": "Data/Stats/HookCoverageView.cs",
      "line": 50,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_hookcoverageview_cs_hookcoverageview_",
      "name": "HookCoverageView.MeasuredHooks",
      "file": "Data/Stats/HookCoverageView.cs",
      "line": 59,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_hookcoverageview_cs_hookcoverageview_",
      "name": "HookCoverageView.MeasuredForMod",
      "file": "Data/Stats/HookCoverageView.cs",
      "line": 80,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_stats_hookcoverageview_cs_hookcoverageview_",
      "name": "HookCoverageView.TotalForMod",
      "file": "Data/Stats/HookCoverageView.cs",
      "line": 87,
      "in": 2,
      "out": 0
    },
    {
      "id": "data_stats_kpicalculator_live_cs_kpicalculator_c",
      "name": "KpiCalculator.Compute",
      "file": "Data/Stats/KpiCalculator.Live.cs",
      "line": 20,
      "in": 8,
      "out": 1
    },
    {
      "id": "data_stats_kpicalculator_cs_kpicalculator_comput",
      "name": "KpiCalculator.ComputeCore",
      "file": "Data/Stats/KpiCalculator.cs",
      "line": 46,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_stats_kpistat_cs_kpistat_initialise_45",
      "name": "KpiStat.Initialise",
      "file": "Data/Stats/KpiStat.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_kpistat_cs_kpistat_reset_46",
      "name": "KpiStat.Reset",
      "file": "Data/Stats/KpiStat.cs",
      "line": 46,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_kpistat_cs_kpistat_dispose_47",
      "name": "KpiStat.Dispose",
      "file": "Data/Stats/KpiStat.cs",
      "line": 47,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_kpistat_cs_kpistat_currentsnapshot_49",
      "name": "KpiStat.CurrentSnapshot",
      "file": "Data/Stats/KpiStat.cs",
      "line": 49,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_stats_memorytrend_cs_memorytrendsnapshot_ct",
      "name": "MemoryTrendSnapshot()",
      "file": "Data/Stats/MemoryTrend.cs",
      "line": 34,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_memorytrend_cs_memorytrend_push_93",
      "name": "MemoryTrend.Push",
      "file": "Data/Stats/MemoryTrend.cs",
      "line": 93,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_memorytrend_cs_memorytrend_snapshot_1",
      "name": "MemoryTrend.Snapshot",
      "file": "Data/Stats/MemoryTrend.cs",
      "line": 109,
      "in": 0,
      "out": 3
    },
    {
      "id": "data_stats_memorytrend_cs_memorytrend_slopembper",
      "name": "MemoryTrend.SlopeMbPerMin",
      "file": "Data/Stats/MemoryTrend.cs",
      "line": 140,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_memorytrend_cs_memorytrend_classify_1",
      "name": "MemoryTrend.Classify",
      "file": "Data/Stats/MemoryTrend.cs",
      "line": 192,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpact_ctor_48",
      "name": "ModImpact()",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 48,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_ct",
      "name": "ModImpactScorer()",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 146,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_ma",
      "name": "ModImpactScorer.MarkDirty",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 170,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_re",
      "name": "ModImpactScorer.Recompute",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 178,
      "in": 0,
      "out": 3
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_co",
      "name": "ModImpactScorer.ComputeImpacts",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 203,
      "in": 1,
      "out": 2
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_so",
      "name": "ModImpactScorer.Sort",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 262,
      "in": 33,
      "out": 1
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_va",
      "name": "ModImpactScorer.ValueFor",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 286,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_en",
      "name": "ModImpactScorer.EnsureCapacity",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 294,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_modimpactscorer_cs_modimpactscorer_up",
      "name": "ModImpactScorer.UpdateCalibration",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 303,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_modimpactscorer_cs_sortedview_ctor_35",
      "name": "SortedView()",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 352,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_modimpactscorer_cs_sortedview_getenum",
      "name": "SortedView.GetEnumerator",
      "file": "Data/Stats/ModImpactScorer.cs",
      "line": 356,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_permodcontextattendancestat_cs_permod",
      "name": "PerModContextAttendanceStat.Initialise",
      "file": "Data/Stats/PerModContextAttendanceStat.cs",
      "line": 35,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_permodcontextattendancestat_cs_permod",
      "name": "PerModContextAttendanceStat.Reset",
      "file": "Data/Stats/PerModContextAttendanceStat.cs",
      "line": 36,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_permodcontextattendancestat_cs_permod",
      "name": "PerModContextAttendanceStat.Dispose",
      "file": "Data/Stats/PerModContextAttendanceStat.cs",
      "line": 37,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_permodcontextattendancestat_cs_permod",
      "name": "PerModContextAttendanceStat.CurrentSnapshot",
      "file": "Data/Stats/PerModContextAttendanceStat.cs",
      "line": 39,
      "in": 0,
      "out": 3
    },
    {
      "id": "data_stats_persegmentlagdensitystat_cs_persegmen",
      "name": "PerSegmentLagDensityStat.Initialise",
      "file": "Data/Stats/PerSegmentLagDensityStat.cs",
      "line": 40,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_stats_persegmentlagdensitystat_cs_persegmen",
      "name": "PerSegmentLagDensityStat.Reset",
      "file": "Data/Stats/PerSegmentLagDensityStat.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_persegmentlagdensitystat_cs_persegmen",
      "name": "PerSegmentLagDensityStat.Dispose",
      "file": "Data/Stats/PerSegmentLagDensityStat.cs",
      "line": 46,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_persegmentlagdensitystat_cs_persegmen",
      "name": "PerSegmentLagDensityStat.CurrentSnapshot",
      "file": "Data/Stats/PerSegmentLagDensityStat.cs",
      "line": 48,
      "in": 0,
      "out": 4
    },
    {
      "id": "data_stats_realtimespeed_cs_realtimespeed_fold_5",
      "name": "RealtimeSpeed.Fold",
      "file": "Data/Stats/RealtimeSpeed.cs",
      "line": 54,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_realtimespeed_cs_realtimespeed_speedf",
      "name": "RealtimeSpeed.SpeedFrom",
      "file": "Data/Stats/RealtimeSpeed.cs",
      "line": 63,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_realtimespeed_cs_realtimespeed_defici",
      "name": "RealtimeSpeed.DeficitMsPerSecond",
      "file": "Data/Stats/RealtimeSpeed.cs",
      "line": 71,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_selfhealthstat_cs_selfhealthsnapshot_",
      "name": "SelfHealthSnapshot()",
      "file": "Data/Stats/SelfHealthStat.cs",
      "line": 45,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_selfhealthstat_cs_selfhealthsnapshot_",
      "name": "SelfHealthSnapshot.From",
      "file": "Data/Stats/SelfHealthStat.cs",
      "line": 68,
      "in": 1,
      "out": 1
    },
    {
      "id": "data_stats_selfhealthstat_cs_selfhealthstat_init",
      "name": "SelfHealthStat.Initialise",
      "file": "Data/Stats/SelfHealthStat.cs",
      "line": 98,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_selfhealthstat_cs_selfhealthstat_rese",
      "name": "SelfHealthStat.Reset",
      "file": "Data/Stats/SelfHealthStat.cs",
      "line": 99,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_selfhealthstat_cs_selfhealthstat_disp",
      "name": "SelfHealthStat.Dispose",
      "file": "Data/Stats/SelfHealthStat.cs",
      "line": 100,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_selfhealthstat_cs_selfhealthstat_curr",
      "name": "SelfHealthStat.CurrentSnapshot",
      "file": "Data/Stats/SelfHealthStat.cs",
      "line": 102,
      "in": 0,
      "out": 2
    },
    {
      "id": "data_stats_sessionchroniclestat_cs_sessionchroni",
      "name": "SessionChronicleStat.Initialise",
      "file": "Data/Stats/SessionChronicleStat.cs",
      "line": 50,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_sessionchroniclestat_cs_sessionchroni",
      "name": "SessionChronicleStat.Reset",
      "file": "Data/Stats/SessionChronicleStat.cs",
      "line": 51,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_sessionchroniclestat_cs_sessionchroni",
      "name": "SessionChronicleStat.Dispose",
      "file": "Data/Stats/SessionChronicleStat.cs",
      "line": 52,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_sessionchroniclestat_cs_sessionchroni",
      "name": "SessionChronicleStat.CurrentSnapshot",
      "file": "Data/Stats/SessionChronicleStat.cs",
      "line": 54,
      "in": 0,
      "out": 6
    },
    {
      "id": "data_stats_sessionchroniclestat_cs_sessionchroni",
      "name": "SessionChronicleStat.ClassifyTransitionKind",
      "file": "Data/Stats/SessionChronicleStat.cs",
      "line": 190,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_sessionchroniclestat_cs_sessionchroni",
      "name": "SessionChronicleStat.FormatTransition",
      "file": "Data/Stats/SessionChronicleStat.cs",
      "line": 217,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_spikesstat_cs_spikessnapshot_ctor_27",
      "name": "SpikesSnapshot()",
      "file": "Data/Stats/SpikesStat.cs",
      "line": 27,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_spikesstat_cs_spikesstat_initialise_5",
      "name": "SpikesStat.Initialise",
      "file": "Data/Stats/SpikesStat.cs",
      "line": 50,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_spikesstat_cs_spikesstat_reset_51",
      "name": "SpikesStat.Reset",
      "file": "Data/Stats/SpikesStat.cs",
      "line": 51,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_spikesstat_cs_spikesstat_dispose_52",
      "name": "SpikesStat.Dispose",
      "file": "Data/Stats/SpikesStat.cs",
      "line": 52,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_spikesstat_cs_spikesstat_currentsnaps",
      "name": "SpikesStat.CurrentSnapshot",
      "file": "Data/Stats/SpikesStat.cs",
      "line": 54,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_stats_stallsstat_cs_stallssnapshot_ctor_23",
      "name": "StallsSnapshot()",
      "file": "Data/Stats/StallsStat.cs",
      "line": 23,
      "in": 1,
      "out": 0
    },
    {
      "id": "data_stats_stallsstat_cs_stallsstat_initialise_4",
      "name": "StallsStat.Initialise",
      "file": "Data/Stats/StallsStat.cs",
      "line": 44,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_stallsstat_cs_stallsstat_reset_45",
      "name": "StallsStat.Reset",
      "file": "Data/Stats/StallsStat.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_stallsstat_cs_stallsstat_dispose_46",
      "name": "StallsStat.Dispose",
      "file": "Data/Stats/StallsStat.cs",
      "line": 46,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_stallsstat_cs_stallsstat_currentsnaps",
      "name": "StallsStat.CurrentSnapshot",
      "file": "Data/Stats/StallsStat.cs",
      "line": 48,
      "in": 0,
      "out": 1
    },
    {
      "id": "data_stats_transitiontrackstat_cs_transitiontrac",
      "name": "TransitionTrackStat.Initialise",
      "file": "Data/Stats/TransitionTrackStat.cs",
      "line": 47,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_transitiontrackstat_cs_transitiontrac",
      "name": "TransitionTrackStat.Reset",
      "file": "Data/Stats/TransitionTrackStat.cs",
      "line": 48,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_transitiontrackstat_cs_transitiontrac",
      "name": "TransitionTrackStat.Dispose",
      "file": "Data/Stats/TransitionTrackStat.cs",
      "line": 49,
      "in": 0,
      "out": 0
    },
    {
      "id": "data_stats_transitiontrackstat_cs_transitiontrac",
      "name": "TransitionTrackStat.CurrentSnapshot",
      "file": "Data/Stats/TransitionTrackStat.cs",
      "line": 51,
      "in": 0,
      "out": 3
    },
    {
      "id": "insights_collectorinsightinput_cs_collectorinsig",
      "name": "CollectorInsightInput()",
      "file": "Insights/CollectorInsightInput.cs",
      "line": 20,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_crosssession_crosssessiondetectors_cs_u",
      "name": "UnusedAcrossSessionsDetector.Evaluate",
      "file": "Insights/CrossSession/CrossSessionDetectors.cs",
      "line": 22,
      "in": 0,
      "out": 4
    },
    {
      "id": "insights_crosssession_crosssessiondetectors_cs_l",
      "name": "LifetimeSpikeContributorDetector.Evaluate",
      "file": "Insights/CrossSession/CrossSessionDetectors.cs",
      "line": 60,
      "in": 0,
      "out": 5
    },
    {
      "id": "insights_crosssession_crosssessiondetectors_cs_c",
      "name": "CostlyDespiteLowUsageDetector.Evaluate",
      "file": "Insights/CrossSession/CrossSessionDetectors.cs",
      "line": 109,
      "in": 5,
      "out": 7
    },
    {
      "id": "insights_crosssession_crosssessiondetectors_cs_c",
      "name": "CrossModpackCostDivergenceDetector.Evaluate",
      "file": "Insights/CrossSession/CrossSessionDetectors.cs",
      "line": 183,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_crosssession_crosssessionevaluator_cs_c",
      "name": "CrossSessionEvaluator.Run",
      "file": "Insights/CrossSession/CrossSessionEvaluator.cs",
      "line": 32,
      "in": 5,
      "out": 4
    },
    {
      "id": "insights_crosssession_crosssessioninput_cs_cross",
      "name": "CrossSessionInput()",
      "file": "Insights/CrossSession/CrossSessionInput.cs",
      "line": 24,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_crosssession_crosssessioninput_cs_cross",
      "name": "CrossSessionMath.SessionsInWindow",
      "file": "Insights/CrossSession/CrossSessionInput.cs",
      "line": 55,
      "in": 7,
      "out": 0
    },
    {
      "id": "insights_crosssession_crosssessioninput_cs_cross",
      "name": "CrossSessionMath.ActiveCount",
      "file": "Insights/CrossSession/CrossSessionInput.cs",
      "line": 58,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_crosssession_crosssessioninput_cs_cross",
      "name": "CrossSessionMath.AvgCost",
      "file": "Insights/CrossSession/CrossSessionInput.cs",
      "line": 65,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_crosssession_crosssessioninput_cs_cross",
      "name": "CrossSessionMath.AvgEngagement",
      "file": "Insights/CrossSession/CrossSessionInput.cs",
      "line": 79,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_crosssession_crosssessioninput_cs_cross",
      "name": "CrossSessionMath.SumSpikes",
      "file": "Insights/CrossSession/CrossSessionInput.cs",
      "line": 91,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_crosssession_crosssessioninput_cs_cross",
      "name": "CrossSessionMath.Conf",
      "file": "Insights/CrossSession/CrossSessionInput.cs",
      "line": 102,
      "in": 4,
      "out": 0
    },
    {
      "id": "insights_detectors_allocationburstdetector_cs_al",
      "name": "AllocationBurstDetector.IsAvailable",
      "file": "Insights/Detectors/AllocationBurstDetector.cs",
      "line": 44,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_detectors_allocationburstdetector_cs_al",
      "name": "AllocationBurstDetector.Evaluate",
      "file": "Insights/Detectors/AllocationBurstDetector.cs",
      "line": 55,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_detectors_contextconditionalcostdetecto",
      "name": "ContextConditionalCostDetector.IsAvailable",
      "file": "Insights/Detectors/ContextConditionalCostDetector.cs",
      "line": 51,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_contextconditionalcostdetecto",
      "name": "ContextConditionalCostDetector.Evaluate",
      "file": "Insights/Detectors/ContextConditionalCostDetector.cs",
      "line": 67,
      "in": 0,
      "out": 7
    },
    {
      "id": "insights_detectors_contextcorrelatedspikedetecto",
      "name": "ContextCorrelatedSpikeDetector.IsAvailable",
      "file": "Insights/Detectors/ContextCorrelatedSpikeDetector.cs",
      "line": 43,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_contextcorrelatedspikedetecto",
      "name": "ContextCorrelatedSpikeDetector.Evaluate",
      "file": "Insights/Detectors/ContextCorrelatedSpikeDetector.cs",
      "line": 47,
      "in": 0,
      "out": 3
    },
    {
      "id": "insights_detectors_costconcentrationcore_cs_conc",
      "name": "ConcentrationResult()",
      "file": "Insights/Detectors/CostConcentrationCore.cs",
      "line": 31,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_detectors_costconcentrationcore_cs_cost",
      "name": "CostConcentrationCore.Compute",
      "file": "Insights/Detectors/CostConcentrationCore.cs",
      "line": 61,
      "in": 0,
      "out": 7
    },
    {
      "id": "insights_detectors_costconcentrationdetector_cs_",
      "name": "CostConcentrationDetector.IsAvailable",
      "file": "Insights/Detectors/CostConcentrationDetector.cs",
      "line": 37,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_costconcentrationdetector_cs_",
      "name": "CostConcentrationDetector.Evaluate",
      "file": "Insights/Detectors/CostConcentrationDetector.cs",
      "line": 40,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_detectors_drawboundmodcore_cs_drawbound",
      "name": "DrawBoundModResult()",
      "file": "Insights/Detectors/DrawBoundModCore.cs",
      "line": 15,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_detectors_drawboundmodcore_cs_drawbound",
      "name": "DrawBoundModCore.Compute",
      "file": "Insights/Detectors/DrawBoundModCore.cs",
      "line": 56,
      "in": 0,
      "out": 3
    },
    {
      "id": "insights_detectors_drawboundmoddetector_cs_drawb",
      "name": "DrawBoundModDetector.IsAvailable",
      "file": "Insights/Detectors/DrawBoundModDetector.cs",
      "line": 22,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_drawboundmoddetector_cs_drawb",
      "name": "DrawBoundModDetector.Evaluate",
      "file": "Insights/Detectors/DrawBoundModDetector.cs",
      "line": 25,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_detectors_frameheadroomdetector_cs_fram",
      "name": "FrameHeadroomDetector.IsAvailable",
      "file": "Insights/Detectors/FrameHeadroomDetector.cs",
      "line": 48,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_frameheadroomdetector_cs_fram",
      "name": "FrameHeadroomDetector.Evaluate",
      "file": "Insights/Detectors/FrameHeadroomDetector.cs",
      "line": 50,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_detectors_framejitterdetector_cs_framej",
      "name": "FrameJitterDetector.IsAvailable",
      "file": "Insights/Detectors/FrameJitterDetector.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_framejitterdetector_cs_framej",
      "name": "FrameJitterDetector.Evaluate",
      "file": "Insights/Detectors/FrameJitterDetector.cs",
      "line": 35,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_detectors_freeremovalcandidatedetector_",
      "name": "FreeRemovalCandidateDetector.IsAvailable",
      "file": "Insights/Detectors/FreeRemovalCandidateDetector.cs",
      "line": 70,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_freeremovalcandidatedetector_",
      "name": "FreeRemovalCandidateDetector.Evaluate",
      "file": "Insights/Detectors/FreeRemovalCandidateDetector.cs",
      "line": 72,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_detectors_gateddetectors_cs_hookfrequen",
      "name": "HookFrequencyTailDetector.IsAvailable",
      "file": "Insights/Detectors/GatedDetectors.cs",
      "line": 53,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_gateddetectors_cs_hookfrequen",
      "name": "HookFrequencyTailDetector.Evaluate",
      "file": "Insights/Detectors/GatedDetectors.cs",
      "line": 55,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_gcpauseculpritdetector_cs_gcp",
      "name": "GcPauseCulpritDetector.IsAvailable",
      "file": "Insights/Detectors/GcPauseCulpritDetector.cs",
      "line": 67,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_gcpauseculpritdetector_cs_gcp",
      "name": "GcPauseCulpritDetector.Evaluate",
      "file": "Insights/Detectors/GcPauseCulpritDetector.cs",
      "line": 77,
      "in": 0,
      "out": 3
    },
    {
      "id": "insights_detectors_heapleakdetector_cs_heapleakd",
      "name": "HeapLeakDetector.IsAvailable",
      "file": "Insights/Detectors/HeapLeakDetector.cs",
      "line": 41,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_heapleakdetector_cs_heapleakd",
      "name": "HeapLeakDetector.Evaluate",
      "file": "Insights/Detectors/HeapLeakDetector.cs",
      "line": 43,
      "in": 0,
      "out": 3
    },
    {
      "id": "insights_detectors_hothookdominancecore_cs_hotho",
      "name": "HotHookDominanceCore.Evaluate",
      "file": "Insights/Detectors/HotHookDominanceCore.cs",
      "line": 23,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_detectors_hothookdominancedetector_cs_h",
      "name": "HotHookDominanceDetector.IsAvailable",
      "file": "Insights/Detectors/HotHookDominanceDetector.cs",
      "line": 48,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_hothookdominancedetector_cs_h",
      "name": "HotHookDominanceDetector.Evaluate",
      "file": "Insights/Detectors/HotHookDominanceDetector.cs",
      "line": 51,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_detectors_interactioninsightdetectors_c",
      "name": "LoadoutCorrelatedCostDetector.IsAvailable",
      "file": "Insights/Detectors/InteractionInsightDetectors.cs",
      "line": 35,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_interactioninsightdetectors_c",
      "name": "LoadoutCorrelatedCostDetector.Evaluate",
      "file": "Insights/Detectors/InteractionInsightDetectors.cs",
      "line": 39,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_detectors_interactioninsightdetectors_c",
      "name": "EventConditionalCostDetector.IsAvailable",
      "file": "Insights/Detectors/InteractionInsightDetectors.cs",
      "line": 147,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_interactioninsightdetectors_c",
      "name": "EventConditionalCostDetector.Evaluate",
      "file": "Insights/Detectors/InteractionInsightDetectors.cs",
      "line": 151,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_detectors_interactioninsightdetectors_c",
      "name": "LoadoutCombinationCostDetector.IsAvailable",
      "file": "Insights/Detectors/InteractionInsightDetectors.cs",
      "line": 264,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_interactioninsightdetectors_c",
      "name": "LoadoutCombinationCostDetector.Evaluate",
      "file": "Insights/Detectors/InteractionInsightDetectors.cs",
      "line": 268,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_newcontributordetector_cs_new",
      "name": "NewContributorDetector.IsAvailable",
      "file": "Insights/Detectors/NewContributorDetector.cs",
      "line": 37,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_newcontributordetector_cs_new",
      "name": "NewContributorDetector.Evaluate",
      "file": "Insights/Detectors/NewContributorDetector.cs",
      "line": 41,
      "in": 0,
      "out": 6
    },
    {
      "id": "insights_detectors_peakcontributortospikedetecto",
      "name": "PeakContributorToSpikeDetector.IsAvailable",
      "file": "Insights/Detectors/PeakContributorToSpikeDetector.cs",
      "line": 44,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_peakcontributortospikedetecto",
      "name": "PeakContributorToSpikeDetector.Evaluate",
      "file": "Insights/Detectors/PeakContributorToSpikeDetector.cs",
      "line": 46,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_detectors_segmentdeathcorrelationdetect",
      "name": "SegmentDeathCorrelationDetector.IsAvailable",
      "file": "Insights/Detectors/SegmentDeathCorrelationDetector.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_segmentdeathcorrelationdetect",
      "name": "SegmentDeathCorrelationDetector.Evaluate",
      "file": "Insights/Detectors/SegmentDeathCorrelationDetector.cs",
      "line": 51,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_detectors_segmentoutlierdetector_cs_seg",
      "name": "SegmentOutlierDetector.IsAvailable",
      "file": "Insights/Detectors/SegmentOutlierDetector.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_segmentoutlierdetector_cs_seg",
      "name": "SegmentOutlierDetector.Evaluate",
      "file": "Insights/Detectors/SegmentOutlierDetector.cs",
      "line": 51,
      "in": 0,
      "out": 3
    },
    {
      "id": "insights_detectors_segmenttopmoddetector_cs_segm",
      "name": "SegmentTopModDetector.IsAvailable",
      "file": "Insights/Detectors/SegmentTopModDetector.cs",
      "line": 43,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_segmenttopmoddetector_cs_segm",
      "name": "SegmentTopModDetector.Evaluate",
      "file": "Insights/Detectors/SegmentTopModDetector.cs",
      "line": 49,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_detectors_sustainedcostshiftdetector_cs",
      "name": "SustainedCostShiftDetector.IsAvailable",
      "file": "Insights/Detectors/SustainedCostShiftDetector.cs",
      "line": 35,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_sustainedcostshiftdetector_cs",
      "name": "SustainedCostShiftDetector.Evaluate",
      "file": "Insights/Detectors/SustainedCostShiftDetector.cs",
      "line": 39,
      "in": 0,
      "out": 6
    },
    {
      "id": "insights_detectors_sustainedslownesscore_cs_sust",
      "name": "SustainedSlownessResult()",
      "file": "Insights/Detectors/SustainedSlownessCore.cs",
      "line": 22,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_detectors_sustainedslownesscore_cs_sust",
      "name": "SustainedSlownessCore.Compute",
      "file": "Insights/Detectors/SustainedSlownessCore.cs",
      "line": 63,
      "in": 0,
      "out": 3
    },
    {
      "id": "insights_detectors_sustainedslownessdetector_cs_",
      "name": "SustainedSlownessDetector.IsAvailable",
      "file": "Insights/Detectors/SustainedSlownessDetector.cs",
      "line": 30,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_detectors_sustainedslownessdetector_cs_",
      "name": "SustainedSlownessDetector.Evaluate",
      "file": "Insights/Detectors/SustainedSlownessDetector.cs",
      "line": 32,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_drivers_drivers_cs_entitycountdriver_sa",
      "name": "EntityCountDriver.Sample",
      "file": "Insights/Drivers/Drivers.cs",
      "line": 13,
      "in": 4,
      "out": 0
    },
    {
      "id": "insights_drivers_drivers_cs_sessionagedriver_sam",
      "name": "SessionAgeDriver.Sample",
      "file": "Insights/Drivers/Drivers.cs",
      "line": 23,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_insight_cs_subjectref_ctor_152",
      "name": "SubjectRef()",
      "file": "Insights/Insight.cs",
      "line": 152,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_insight_cs_insightcontributor_ctor_188",
      "name": "InsightContributor()",
      "file": "Insights/Insight.cs",
      "line": 188,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_insight_cs_insight_invalidaterenderingc",
      "name": "Insight.InvalidateRenderingCache",
      "file": "Insights/Insight.cs",
      "line": 350,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.Render",
      "file": "Insights/InsightRenderer.cs",
      "line": 55,
      "in": 4,
      "out": 1
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_buil",
      "name": "InsightRenderer.Build",
      "file": "Insights/InsightRenderer.cs",
      "line": 71,
      "in": 1,
      "out": 22
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderSegmentOutlier",
      "file": "Insights/InsightRenderer.cs",
      "line": 100,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderSegmentTopMod",
      "file": "Insights/InsightRenderer.cs",
      "line": 119,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderSegmentDeathCorrelation",
      "file": "Insights/InsightRenderer.cs",
      "line": 137,
      "in": 1,
      "out": 2
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderHotHook",
      "file": "Insights/InsightRenderer.cs",
      "line": 158,
      "in": 1,
      "out": 4
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderAllocBurst",
      "file": "Insights/InsightRenderer.cs",
      "line": 177,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderFreeRemoval",
      "file": "Insights/InsightRenderer.cs",
      "line": 194,
      "in": 1,
      "out": 2
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderPeakContributor",
      "file": "Insights/InsightRenderer.cs",
      "line": 205,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderContextConditionalCost",
      "file": "Insights/InsightRenderer.cs",
      "line": 222,
      "in": 1,
      "out": 4
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderFrameHeadroom",
      "file": "Insights/InsightRenderer.cs",
      "line": 242,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderSustainedSlowness",
      "file": "Insights/InsightRenderer.cs",
      "line": 264,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderDrawBoundMod",
      "file": "Insights/InsightRenderer.cs",
      "line": 296,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_dura",
      "name": "InsightRenderer.Duration",
      "file": "Insights/InsightRenderer.cs",
      "line": 315,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderCostConcentration",
      "file": "Insights/InsightRenderer.cs",
      "line": 324,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderFrameJitter",
      "file": "Insights/InsightRenderer.cs",
      "line": 348,
      "in": 1,
      "out": 2
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderContextCorrelatedSpike",
      "file": "Insights/InsightRenderer.cs",
      "line": 359,
      "in": 1,
      "out": 2
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderHeapLeak",
      "file": "Insights/InsightRenderer.cs",
      "line": 375,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderSustainedCostShift",
      "file": "Insights/InsightRenderer.cs",
      "line": 388,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderNewContributor",
      "file": "Insights/InsightRenderer.cs",
      "line": 400,
      "in": 1,
      "out": 2
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderUnusedAcrossSessions",
      "file": "Insights/InsightRenderer.cs",
      "line": 412,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderLifetimeSpikeContributor",
      "file": "Insights/InsightRenderer.cs",
      "line": 425,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderCostlyDespiteLowUsage",
      "file": "Insights/InsightRenderer.cs",
      "line": 439,
      "in": 1,
      "out": 2
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderCrossModpackCostDivergence",
      "file": "Insights/InsightRenderer.cs",
      "line": 453,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_rend",
      "name": "InsightRenderer.RenderUnsupported",
      "file": "Insights/InsightRenderer.cs",
      "line": 469,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_cont",
      "name": "InsightRenderer.ContextLabel",
      "file": "Insights/InsightRenderer.cs",
      "line": 473,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_mult",
      "name": "InsightRenderer.Multiple",
      "file": "Insights/InsightRenderer.cs",
      "line": 483,
      "in": 5,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_segm",
      "name": "InsightRenderer.SegmentLabel",
      "file": "Insights/InsightRenderer.cs",
      "line": 495,
      "in": 2,
      "out": 1
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_cont",
      "name": "InsightRenderer.ContributorNames",
      "file": "Insights/InsightRenderer.cs",
      "line": 501,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_cont",
      "name": "InsightRenderer.ContributorShares",
      "file": "Insights/InsightRenderer.cs",
      "line": 513,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_modn",
      "name": "InsightRenderer.ModName",
      "file": "Insights/InsightRenderer.cs",
      "line": 524,
      "in": 16,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_hook",
      "name": "InsightRenderer.HookName",
      "file": "Insights/InsightRenderer.cs",
      "line": 531,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_ms_5",
      "name": "InsightRenderer.Ms",
      "file": "Insights/InsightRenderer.cs",
      "line": 538,
      "in": 14,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_pct_",
      "name": "InsightRenderer.Pct",
      "file": "Insights/InsightRenderer.cs",
      "line": 545,
      "in": 10,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_byte",
      "name": "InsightRenderer.Bytes",
      "file": "Insights/InsightRenderer.cs",
      "line": 550,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightrenderer_cs_insightrenderer_base",
      "name": "InsightRenderer.BaselineClause",
      "file": "Insights/InsightRenderer.cs",
      "line": 557,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_insightstore_cs_insightstore_ctor_63",
      "name": "InsightStore()",
      "file": "Insights/InsightStore.cs",
      "line": 63,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightstore_cs_insightstore_ctor_65",
      "name": "InsightStore()",
      "file": "Insights/InsightStore.cs",
      "line": 65,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_insightstore_cs_insightstore_submit_82",
      "name": "InsightStore.Submit",
      "file": "Insights/InsightStore.cs",
      "line": 82,
      "in": 1,
      "out": 5
    },
    {
      "id": "insights_insightstore_cs_insightstore_tick_115",
      "name": "InsightStore.Tick",
      "file": "Insights/InsightStore.cs",
      "line": 115,
      "in": 2,
      "out": 2
    },
    {
      "id": "insights_insightstore_cs_insightstore_alllive_13",
      "name": "InsightStore.AllLive",
      "file": "Insights/InsightStore.cs",
      "line": 132,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_insightstore_cs_insightstore_topinto_15",
      "name": "InsightStore.TopInto",
      "file": "Insights/InsightStore.cs",
      "line": 150,
      "in": 2,
      "out": 4
    },
    {
      "id": "insights_insightstore_cs_insightstore_top_186",
      "name": "InsightStore.Top",
      "file": "Insights/InsightStore.cs",
      "line": 186,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_insightstore_cs_insightstore_evictstale",
      "name": "InsightStore.EvictStalest",
      "file": "Insights/InsightStore.cs",
      "line": 194,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_insightstore_cs_insightstore_stablekey_",
      "name": "InsightStore.StableKey",
      "file": "Insights/InsightStore.cs",
      "line": 220,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightstore_cs_insightstore_promotecon",
      "name": "InsightStore.PromoteConfidence",
      "file": "Insights/InsightStore.cs",
      "line": 231,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_getorc",
      "name": "InsightsEngine.GetOrCreateShared",
      "file": "Insights/InsightsEngine.cs",
      "line": 61,
      "in": 2,
      "out": 2
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_ctor_9",
      "name": "InsightsEngine()",
      "file": "Insights/InsightsEngine.cs",
      "line": 97,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_buildg",
      "name": "InsightsEngine.BuildGatedMap",
      "file": "Insights/InsightsEngine.cs",
      "line": 151,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_buildg",
      "name": "InsightsEngine.BuildGatedLabel",
      "file": "Insights/InsightsEngine.cs",
      "line": 169,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_setcro",
      "name": "InsightsEngine.SetCrossSessionInsights",
      "file": "Insights/InsightsEngine.cs",
      "line": 197,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_seedco",
      "name": "InsightsEngine.SeedContextBaseline",
      "file": "Insights/InsightsEngine.cs",
      "line": 218,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_evalua",
      "name": "InsightsEngine.Evaluate",
      "file": "Insights/InsightsEngine.cs",
      "line": 232,
      "in": 0,
      "out": 6
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_update",
      "name": "InsightsEngine.UpdateContextBaseline",
      "file": "Insights/InsightsEngine.cs",
      "line": 261,
      "in": 1,
      "out": 10
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_anybos",
      "name": "InsightsEngine.AnyBoss",
      "file": "Insights/InsightsEngine.cs",
      "line": 313,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_insightsengine_cs_insightsengine_gatedp",
      "name": "InsightsEngine.GatedPatterns",
      "file": "Insights/InsightsEngine.cs",
      "line": 328,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_crosscuttingsignalstat_cs_cross",
      "name": "CrossCuttingSignalStat.Initialise",
      "file": "Insights/Publish/CrossCuttingSignalStat.cs",
      "line": 35,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_crosscuttingsignalstat_cs_cross",
      "name": "CrossCuttingSignalStat.Reset",
      "file": "Insights/Publish/CrossCuttingSignalStat.cs",
      "line": 36,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_crosscuttingsignalstat_cs_cross",
      "name": "CrossCuttingSignalStat.Dispose",
      "file": "Insights/Publish/CrossCuttingSignalStat.cs",
      "line": 37,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_crosscuttingsignalstat_cs_cross",
      "name": "CrossCuttingSignalStat.CurrentSnapshot",
      "file": "Insights/Publish/CrossCuttingSignalStat.cs",
      "line": 39,
      "in": 0,
      "out": 5
    },
    {
      "id": "insights_publish_dormantsurfacestat_cs_dormantsu",
      "name": "DormantSurfaceStat.Initialise",
      "file": "Insights/Publish/DormantSurfaceStat.cs",
      "line": 37,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_dormantsurfacestat_cs_dormantsu",
      "name": "DormantSurfaceStat.Reset",
      "file": "Insights/Publish/DormantSurfaceStat.cs",
      "line": 38,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_dormantsurfacestat_cs_dormantsu",
      "name": "DormantSurfaceStat.Dispose",
      "file": "Insights/Publish/DormantSurfaceStat.cs",
      "line": 39,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_dormantsurfacestat_cs_dormantsu",
      "name": "DormantSurfaceStat.CurrentSnapshot",
      "file": "Insights/Publish/DormantSurfaceStat.cs",
      "line": 41,
      "in": 0,
      "out": 7
    },
    {
      "id": "insights_publish_dormantsurfacestat_cs_dormantsu",
      "name": "DormantSurfaceStat.DominantUnusedCategory",
      "file": "Insights/Publish/DormantSurfaceStat.cs",
      "line": 113,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_publish_engagementcostscatterstat_cs_en",
      "name": "EngagementCostScatterStat.Initialise",
      "file": "Insights/Publish/EngagementCostScatterStat.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_engagementcostscatterstat_cs_en",
      "name": "EngagementCostScatterStat.Reset",
      "file": "Insights/Publish/EngagementCostScatterStat.cs",
      "line": 34,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_engagementcostscatterstat_cs_en",
      "name": "EngagementCostScatterStat.Dispose",
      "file": "Insights/Publish/EngagementCostScatterStat.cs",
      "line": 35,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_engagementcostscatterstat_cs_en",
      "name": "EngagementCostScatterStat.CurrentSnapshot",
      "file": "Insights/Publish/EngagementCostScatterStat.cs",
      "line": 37,
      "in": 0,
      "out": 6
    },
    {
      "id": "insights_publish_insightsstat_cs_insightssnapsho",
      "name": "InsightsSnapshot()",
      "file": "Insights/Publish/InsightsStat.cs",
      "line": 27,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_publish_insightsstat_cs_insightsstat_in",
      "name": "InsightsStat.Initialise",
      "file": "Insights/Publish/InsightsStat.cs",
      "line": 51,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_insightsstat_cs_insightsstat_re",
      "name": "InsightsStat.Reset",
      "file": "Insights/Publish/InsightsStat.cs",
      "line": 52,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_insightsstat_cs_insightsstat_di",
      "name": "InsightsStat.Dispose",
      "file": "Insights/Publish/InsightsStat.cs",
      "line": 53,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_insightsstat_cs_insightsstat_cu",
      "name": "InsightsStat.CurrentSnapshot",
      "file": "Insights/Publish/InsightsStat.cs",
      "line": 55,
      "in": 0,
      "out": 2
    },
    {
      "id": "insights_publish_modinteractionaggregator_cs_mod",
      "name": "ModInteractionAggregator.Initialise",
      "file": "Insights/Publish/ModInteractionAggregator.cs",
      "line": 48,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_modinteractionaggregator_cs_mod",
      "name": "ModInteractionAggregator.Reset",
      "file": "Insights/Publish/ModInteractionAggregator.cs",
      "line": 54,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_modinteractionaggregator_cs_mod",
      "name": "ModInteractionAggregator.Dispose",
      "file": "Insights/Publish/ModInteractionAggregator.cs",
      "line": 60,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_modinteractionaggregator_cs_mod",
      "name": "ModInteractionAggregator.CurrentSnapshot",
      "file": "Insights/Publish/ModInteractionAggregator.cs",
      "line": 62,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_publish_modinteractionaggregator_cs_mod",
      "name": "ModInteractionAggregator.Compute",
      "file": "Insights/Publish/ModInteractionAggregator.cs",
      "line": 85,
      "in": 1,
      "out": 5
    },
    {
      "id": "insights_publish_modinteractionaggregator_cs_mod",
      "name": "ModInteractionAggregator.Pearson",
      "file": "Insights/Publish/ModInteractionAggregator.cs",
      "line": 202,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_publish_modobservatorystat_cs_modobserv",
      "name": "ModObservatoryStat.Initialise",
      "file": "Insights/Publish/ModObservatoryStat.cs",
      "line": 52,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_modobservatorystat_cs_modobserv",
      "name": "ModObservatoryStat.Reset",
      "file": "Insights/Publish/ModObservatoryStat.cs",
      "line": 53,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_modobservatorystat_cs_modobserv",
      "name": "ModObservatoryStat.Dispose",
      "file": "Insights/Publish/ModObservatoryStat.cs",
      "line": 54,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_publish_modobservatorystat_cs_modobserv",
      "name": "ModObservatoryStat.CurrentSnapshot",
      "file": "Insights/Publish/ModObservatoryStat.cs",
      "line": 56,
      "in": 0,
      "out": 8
    },
    {
      "id": "insights_publish_modobservatorystat_cs_modobserv",
      "name": "ModObservatoryStat.BuildBiomeAttendance",
      "file": "Insights/Publish/ModObservatoryStat.cs",
      "line": 176,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_publish_modobservatorystat_cs_modobserv",
      "name": "ModObservatoryStat.BuildTopLoadoutItems",
      "file": "Insights/Publish/ModObservatoryStat.cs",
      "line": 214,
      "in": 1,
      "out": 4
    },
    {
      "id": "insights_publish_modobservatorystat_cs_modobserv",
      "name": "ModObservatoryStat.AccumulateItem",
      "file": "Insights/Publish/ModObservatoryStat.cs",
      "line": 296,
      "in": 1,
      "out": 2
    },
    {
      "id": "insights_publish_modobservatorystat_cs_modobserv",
      "name": "ModObservatoryStat.ResolveModId",
      "file": "Insights/Publish/ModObservatoryStat.cs",
      "line": 317,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_score_43",
      "name": "RankingScorer.Score",
      "file": "Insights/RankingScorer.cs",
      "line": 43,
      "in": 1,
      "out": 6
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_normalis",
      "name": "RankingScorer.NormaliseMagnitude",
      "file": "Insights/RankingScorer.cs",
      "line": 79,
      "in": 1,
      "out": 3
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_issharep",
      "name": "RankingScorer.IsSharePattern",
      "file": "Insights/RankingScorer.cs",
      "line": 88,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_ratiocur",
      "name": "RankingScorer.RatioCurve",
      "file": "Insights/RankingScorer.cs",
      "line": 111,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_clampuni",
      "name": "RankingScorer.ClampUnit",
      "file": "Insights/RankingScorer.cs",
      "line": 118,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_confiden",
      "name": "RankingScorer.ConfidenceWeight",
      "file": "Insights/RankingScorer.cs",
      "line": 120,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_recencyw",
      "name": "RankingScorer.RecencyWeight",
      "file": "Insights/RankingScorer.cs",
      "line": 128,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_actionab",
      "name": "RankingScorer.Actionability",
      "file": "Insights/RankingScorer.cs",
      "line": 135,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_novelty_",
      "name": "RankingScorer.Novelty",
      "file": "Insights/RankingScorer.cs",
      "line": 150,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_rankingscorer_cs_rankingscorer_audience",
      "name": "RankingScorer.AudienceMatch",
      "file": "Insights/RankingScorer.cs",
      "line": 157,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_referenceframes_contextbaseline_cs_cont",
      "name": "ContextBaseline()",
      "file": "Insights/ReferenceFrames/ContextBaseline.cs",
      "line": 54,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_referenceframes_contextbaseline_cs_cont",
      "name": "ContextBaseline.ObserveSpikes",
      "file": "Insights/ReferenceFrames/ContextBaseline.cs",
      "line": 82,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_referenceframes_contextbaseline_cs_cont",
      "name": "ContextBaseline.Observe",
      "file": "Insights/ReferenceFrames/ContextBaseline.cs",
      "line": 123,
      "in": 1,
      "out": 2
    },
    {
      "id": "insights_referenceframes_contextbaseline_cs_cont",
      "name": "ContextBaseline.TryConditional",
      "file": "Insights/ReferenceFrames/ContextBaseline.cs",
      "line": 144,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_referenceframes_contextbaseline_cs_cont",
      "name": "ContextBaseline.Seed",
      "file": "Insights/ReferenceFrames/ContextBaseline.cs",
      "line": 188,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_referenceframes_contextbaseline_cs_cont",
      "name": "ContextBaseline.GetOrAddBucket",
      "file": "Insights/ReferenceFrames/ContextBaseline.cs",
      "line": 202,
      "in": 2,
      "out": 1
    },
    {
      "id": "insights_referenceframes_contextbaseline_cs_cont",
      "name": "ContextBaseline.EvictLeastSampled",
      "file": "Insights/ReferenceFrames/ContextBaseline.cs",
      "line": 212,
      "in": 1,
      "out": 1
    },
    {
      "id": "insights_referenceframes_temporalbaseline_cs_tem",
      "name": "TemporalBaseline()",
      "file": "Insights/ReferenceFrames/TemporalBaseline.cs",
      "line": 38,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_referenceframes_temporalbaseline_cs_tem",
      "name": "TemporalBaseline.Observe",
      "file": "Insights/ReferenceFrames/TemporalBaseline.cs",
      "line": 57,
      "in": 0,
      "out": 1
    },
    {
      "id": "insights_referenceframes_temporalbaseline_cs_tem",
      "name": "TemporalBaseline.TryPerMod",
      "file": "Insights/ReferenceFrames/TemporalBaseline.cs",
      "line": 76,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_shared_modmetrics_cs_modmetrics_usagewe",
      "name": "ModMetrics.UsageWeight",
      "file": "Insights/Shared/ModMetrics.cs",
      "line": 43,
      "in": 4,
      "out": 0
    },
    {
      "id": "insights_shared_modmetrics_cs_modmetrics_creatio",
      "name": "ModMetrics.CreationWeight",
      "file": "Insights/Shared/ModMetrics.cs",
      "line": 53,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_shared_modmetrics_cs_modmetrics_rosters",
      "name": "ModMetrics.RosterSize",
      "file": "Insights/Shared/ModMetrics.cs",
      "line": 63,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_shared_modmetrics_cs_modmetrics_summodc",
      "name": "ModMetrics.SumModCategories",
      "file": "Insights/Shared/ModMetrics.cs",
      "line": 74,
      "in": 4,
      "out": 0
    },
    {
      "id": "insights_shared_modnames_cs_modnames_safename_20",
      "name": "ModNames.SafeName",
      "file": "Insights/Shared/ModNames.cs",
      "line": 20,
      "in": 3,
      "out": 0
    },
    {
      "id": "insights_shared_modnames_cs_modnames_safename_28",
      "name": "ModNames.SafeName",
      "file": "Insights/Shared/ModNames.cs",
      "line": 28,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_shared_shares_cs_shares_safeshare_23",
      "name": "Shares.SafeShare",
      "file": "Insights/Shared/Shares.cs",
      "line": 23,
      "in": 5,
      "out": 0
    },
    {
      "id": "insights_shared_shares_cs_shares_safeshare_31",
      "name": "Shares.SafeShare",
      "file": "Insights/Shared/Shares.cs",
      "line": 31,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_shared_shares_cs_shares_percentage_35",
      "name": "Shares.Percentage",
      "file": "Insights/Shared/Shares.cs",
      "line": 35,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_shared_shares_cs_shares_truncate_43",
      "name": "Shares.Truncate",
      "file": "Insights/Shared/Shares.cs",
      "line": 43,
      "in": 10,
      "out": 0
    },
    {
      "id": "insights_shared_shares_cs_shares_topn_54",
      "name": "Shares.TopN",
      "file": "Insights/Shared/Shares.cs",
      "line": 54,
      "in": 2,
      "out": 2
    },
    {
      "id": "insights_shared_shares_cs_shares_paretocount_67",
      "name": "Shares.ParetoCount",
      "file": "Insights/Shared/Shares.cs",
      "line": 67,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_shared_stats_cs_runningstat_add_20",
      "name": "RunningStat.Add",
      "file": "Insights/Shared/Stats.cs",
      "line": 20,
      "in": 0,
      "out": 0
    },
    {
      "id": "insights_shared_stats_cs_runningstat_fromcompone",
      "name": "RunningStat.FromComponents",
      "file": "Insights/Shared/Stats.cs",
      "line": 41,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_shared_stats_cs_runningstat_merge_50",
      "name": "RunningStat.Merge",
      "file": "Insights/Shared/Stats.cs",
      "line": 50,
      "in": 2,
      "out": 0
    },
    {
      "id": "insights_shared_stats_cs_runningstat_without_85",
      "name": "RunningStat.Without",
      "file": "Insights/Shared/Stats.cs",
      "line": 85,
      "in": 1,
      "out": 0
    },
    {
      "id": "insights_shared_stats_cs_stats_cohensd_120",
      "name": "Stats.CohensD",
      "file": "Insights/Shared/Stats.cs",
      "line": 120,
      "in": 4,
      "out": 0
    },
    {
      "id": "insights_shared_stats_cs_stats_welchttestp_139",
      "name": "Stats.WelchTTestP",
      "file": "Insights/Shared/Stats.cs",
      "line": 139,
      "in": 4,
      "out": 0
    },
    {
      "id": "insights_shared_stats_cs_stats_erf_157",
      "name": "Stats.Erf",
      "file": "Insights/Shared/Stats.cs",
      "line": 157,
      "in": 0,
      "out": 0
    },
    {
      "id": "performanceprofiler_cs_performanceprofiler_load_",
      "name": "PerformanceProfiler.Load",
      "file": "PerformanceProfiler.cs",
      "line": 60,
      "in": 1,
      "out": 4
    },
    {
      "id": "performanceprofiler_cs_performanceprofiler_regis",
      "name": "PerformanceProfiler.RegisterDataPipeline",
      "file": "PerformanceProfiler.cs",
      "line": 145,
      "in": 1,
      "out": 1
    },
    {
      "id": "performanceprofiler_cs_performanceprofiler_unloa",
      "name": "PerformanceProfiler.Unload",
      "file": "PerformanceProfiler.cs",
      "line": 202,
      "in": 0,
      "out": 3
    },
    {
      "id": "performanceprofiler_cs_profilerplayer_onenterwor",
      "name": "ProfilerPlayer.OnEnterWorld",
      "file": "PerformanceProfiler.cs",
      "line": 250,
      "in": 0,
      "out": 0
    },
    {
      "id": "performanceprofiler_cs_profilerplayer_processtri",
      "name": "ProfilerPlayer.ProcessTriggers",
      "file": "PerformanceProfiler.cs",
      "line": 271,
      "in": 0,
      "out": 1
    },
    {
      "id": "performanceprofiler_cs_profilerplayer_opendashbo",
      "name": "ProfilerPlayer.OpenDashboardInBrowser",
      "file": "PerformanceProfiler.cs",
      "line": 301,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_bsonshortnames_cs_bsonshortnames_app",
      "name": "BsonShortNames.Apply",
      "file": "Persistence/BsonShortNames.cs",
      "line": 46,
      "in": 7,
      "out": 0
    },
    {
      "id": "persistence_commands_profilerreportcommand_cs_pr",
      "name": "ProfilerReportCommand.Action",
      "file": "Persistence/Commands/ProfilerReportCommand.cs",
      "line": 21,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_commands_querychatcommands_cs_profil",
      "name": "ProfilerSummaryCommand.Action",
      "file": "Persistence/Commands/QueryChatCommands.cs",
      "line": 31,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_commands_querychatcommands_cs_profil",
      "name": "ProfilerStallsCommand.Action",
      "file": "Persistence/Commands/QueryChatCommands.cs",
      "line": 77,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_commands_querychatcommands_cs_profil",
      "name": "ProfilerModsCommand.Action",
      "file": "Persistence/Commands/QueryChatCommands.cs",
      "line": 110,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_commands_querychatcommands_cs_profil",
      "name": "ProfilerDeathsCommand.Action",
      "file": "Persistence/Commands/QueryChatCommands.cs",
      "line": 140,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_commands_querychatcommands_cs_profil",
      "name": "ProfilerTailCommand.Action",
      "file": "Persistence/Commands/QueryChatCommands.cs",
      "line": 168,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_commands_querychatcommands_cs_profil",
      "name": "ProfilerTimelineCommand.Action",
      "file": "Persistence/Commands/QueryChatCommands.cs",
      "line": 223,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_commands_querychatcommands_cs_profil",
      "name": "ProfilerBookmarkCommand.Action",
      "file": "Persistence/Commands/QueryChatCommands.cs",
      "line": 261,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_commands_querychatcommands_cs_profil",
      "name": "ProfilerBookmarkEndCommand.Action",
      "file": "Persistence/Commands/QueryChatCommands.cs",
      "line": 288,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_commands_querycommandbase_cs_queryco",
      "name": "QueryCommandBase.GetDb",
      "file": "Persistence/Commands/QueryCommandBase.cs",
      "line": 30,
      "in": 5,
      "out": 0
    },
    {
      "id": "persistence_commands_querycommandbase_cs_queryco",
      "name": "QueryCommandBase.CurrentOrLatestSessionId",
      "file": "Persistence/Commands/QueryCommandBase.cs",
      "line": 43,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_commands_querycommandbase_cs_queryco",
      "name": "QueryCommandBase.FormatDuration",
      "file": "Persistence/Commands/QueryCommandBase.cs",
      "line": 60,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_commands_querycommandbase_cs_queryco",
      "name": "QueryCommandBase.ParseCountArg",
      "file": "Persistence/Commands/QueryCommandBase.cs",
      "line": 70,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_commands_querycommandbase_cs_queryco",
      "name": "QueryCommandBase.SafeRun",
      "file": "Persistence/Commands/QueryCommandBase.cs",
      "line": 87,
      "in": 8,
      "out": 0
    },
    {
      "id": "persistence_contexttransitionwatcher_cs_contextt",
      "name": "ContextTransitionWatcher.OnSnapshot",
      "file": "Persistence/ContextTransitionWatcher.cs",
      "line": 65,
      "in": 1,
      "out": 7
    },
    {
      "id": "persistence_contexttransitionwatcher_cs_contextt",
      "name": "ContextTransitionWatcher.DiffBiomeBits",
      "file": "Persistence/ContextTransitionWatcher.cs",
      "line": 220,
      "in": 1,
      "out": 4
    },
    {
      "id": "persistence_contexttransitionwatcher_cs_contextt",
      "name": "ContextTransitionWatcher.ClassifyBossOutcome",
      "file": "Persistence/ContextTransitionWatcher.cs",
      "line": 253,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_contexttransitionwatcher_cs_contextt",
      "name": "ContextTransitionWatcher.DescribeContext",
      "file": "Persistence/ContextTransitionWatcher.cs",
      "line": 274,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_crosssessionstore_cs_crosssessionsto",
      "name": "CrossSessionStore.Load",
      "file": "Persistence/CrossSessionStore.cs",
      "line": 33,
      "in": 0,
      "out": 4
    },
    {
      "id": "persistence_crosssessionstore_cs_crosssessionsto",
      "name": "CrossSessionStore.Save",
      "file": "Persistence/CrossSessionStore.cs",
      "line": 53,
      "in": 1,
      "out": 4
    },
    {
      "id": "persistence_crosssessionstore_cs_crosssessionsto",
      "name": "CrossSessionStore.ToRow",
      "file": "Persistence/CrossSessionStore.cs",
      "line": 91,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_dbreadmodel_cs_dbreadmodel_getlastse",
      "name": "DbReadModel.GetLastSession",
      "file": "Persistence/DbReadModel.cs",
      "line": 41,
      "in": 4,
      "out": 0
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_ctor_71",
      "name": "DbWriteOp()",
      "file": "Persistence/DbWriteOp.cs",
      "line": 71,
      "in": 25,
      "out": 0
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_sessionstart_",
      "name": "DbWriteOp.SessionStart",
      "file": "Persistence/DbWriteOp.cs",
      "line": 82,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_sessionend_85",
      "name": "DbWriteOp.SessionEnd",
      "file": "Persistence/DbWriteOp.cs",
      "line": 85,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_spike_88",
      "name": "DbWriteOp.Spike",
      "file": "Persistence/DbWriteOp.cs",
      "line": 88,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_stall_91",
      "name": "DbWriteOp.Stall",
      "file": "Persistence/DbWriteOp.cs",
      "line": 91,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_contexttransi",
      "name": "DbWriteOp.ContextTransition",
      "file": "Persistence/DbWriteOp.cs",
      "line": 94,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_warmaggregate",
      "name": "DbWriteOp.WarmAggregate",
      "file": "Persistence/DbWriteOp.cs",
      "line": 97,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_coldaggregate",
      "name": "DbWriteOp.ColdAggregate",
      "file": "Persistence/DbWriteOp.cs",
      "line": 100,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_archiveaggreg",
      "name": "DbWriteOp.ArchiveAggregate",
      "file": "Persistence/DbWriteOp.cs",
      "line": 103,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_modaggregateb",
      "name": "DbWriteOp.ModAggregateBatch",
      "file": "Persistence/DbWriteOp.cs",
      "line": 106,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_hookaggregate",
      "name": "DbWriteOp.HookAggregateBatch",
      "file": "Persistence/DbWriteOp.cs",
      "line": 109,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_insight_112",
      "name": "DbWriteOp.Insight",
      "file": "Persistence/DbWriteOp.cs",
      "line": 112,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_upsertworld_1",
      "name": "DbWriteOp.UpsertWorld",
      "file": "Persistence/DbWriteOp.cs",
      "line": 115,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_upsertmodlist",
      "name": "DbWriteOp.UpsertModlist",
      "file": "Persistence/DbWriteOp.cs",
      "line": 118,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_upsertmod_121",
      "name": "DbWriteOp.UpsertMod",
      "file": "Persistence/DbWriteOp.cs",
      "line": 121,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_stallcluster_",
      "name": "DbWriteOp.StallCluster",
      "file": "Persistence/DbWriteOp.cs",
      "line": 124,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_playerdeath_1",
      "name": "DbWriteOp.PlayerDeath",
      "file": "Persistence/DbWriteOp.cs",
      "line": 127,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_worldsnapshot",
      "name": "DbWriteOp.WorldSnapshot",
      "file": "Persistence/DbWriteOp.cs",
      "line": 130,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_damagetaken_1",
      "name": "DbWriteOp.DamageTaken",
      "file": "Persistence/DbWriteOp.cs",
      "line": 133,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_damagedealt_1",
      "name": "DbWriteOp.DamageDealt",
      "file": "Persistence/DbWriteOp.cs",
      "line": 136,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_npcspawn_139",
      "name": "DbWriteOp.NpcSpawn",
      "file": "Persistence/DbWriteOp.cs",
      "line": 139,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_itemcreated_1",
      "name": "DbWriteOp.ItemCreated",
      "file": "Persistence/DbWriteOp.cs",
      "line": 142,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_loadoutsnapsh",
      "name": "DbWriteOp.LoadoutSnapshot",
      "file": "Persistence/DbWriteOp.cs",
      "line": 145,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_buffevent_148",
      "name": "DbWriteOp.BuffEvent",
      "file": "Persistence/DbWriteOp.cs",
      "line": 148,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_segment_151",
      "name": "DbWriteOp.Segment",
      "file": "Persistence/DbWriteOp.cs",
      "line": 151,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriteop_cs_dbwriteop_rollupfold_15",
      "name": "DbWriteOp.RollupFold",
      "file": "Persistence/DbWriteOp.cs",
      "line": 154,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_dbwriterthread_cs_dbwriterthread_cto",
      "name": "DbWriterThread()",
      "file": "Persistence/DbWriterThread.cs",
      "line": 80,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_dbwriterthread_cs_dbwriterthread_enq",
      "name": "DbWriterThread.Enqueue",
      "file": "Persistence/DbWriterThread.cs",
      "line": 104,
      "in": 19,
      "out": 1
    },
    {
      "id": "persistence_dbwriterthread_cs_dbwriterthread_run",
      "name": "DbWriterThread.Run",
      "file": "Persistence/DbWriterThread.cs",
      "line": 119,
      "in": 0,
      "out": 8
    },
    {
      "id": "persistence_dbwriterthread_cs_dbwriterthread_may",
      "name": "DbWriterThread.MaybeCheckpoint",
      "file": "Persistence/DbWriterThread.cs",
      "line": 194,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_dbwriterthread_cs_dbwriterthread_dra",
      "name": "DbWriterThread.DrainAndShutdown",
      "file": "Persistence/DbWriterThread.cs",
      "line": 215,
      "in": 1,
      "out": 6
    },
    {
      "id": "persistence_dbwriterthread_cs_dbwriterthread_dis",
      "name": "DbWriterThread.Dispose",
      "file": "Persistence/DbWriterThread.cs",
      "line": 244,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_eventjournal_cs_eventjournal_ctor_51",
      "name": "EventJournal()",
      "file": "Persistence/EventJournal.cs",
      "line": 51,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_eventjournal_cs_eventjournal_appendb",
      "name": "EventJournal.AppendBatch",
      "file": "Persistence/EventJournal.cs",
      "line": 62,
      "in": 2,
      "out": 2
    },
    {
      "id": "persistence_eventjournal_cs_eventjournal_flush_1",
      "name": "EventJournal.Flush",
      "file": "Persistence/EventJournal.cs",
      "line": 105,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_eventjournal_cs_eventjournal_replay_",
      "name": "EventJournal.Replay",
      "file": "Persistence/EventJournal.cs",
      "line": 117,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_eventjournal_cs_eventjournal_truncat",
      "name": "EventJournal.TruncateOnCleanShutdown",
      "file": "Persistence/EventJournal.cs",
      "line": 135,
      "in": 3,
      "out": 1
    },
    {
      "id": "persistence_eventjournal_cs_eventjournal_dispose",
      "name": "EventJournal.Dispose",
      "file": "Persistence/EventJournal.cs",
      "line": 149,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_eventjournal_cs_eventjournal_seriali",
      "name": "EventJournal.SerializePayload",
      "file": "Persistence/EventJournal.cs",
      "line": 157,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_fingerprintcore_cs_fingerprintcore_c",
      "name": "FingerprintCore.Compute",
      "file": "Persistence/FingerprintCore.cs",
      "line": 44,
      "in": 0,
      "out": 4
    },
    {
      "id": "persistence_fingerprintcore_cs_fingerprintcore_h",
      "name": "FingerprintCore.Hash",
      "file": "Persistence/FingerprintCore.cs",
      "line": 64,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_history_historystore_cs_historystore",
      "name": "HistoryStore()",
      "file": "Persistence/History/HistoryStore.cs",
      "line": 38,
      "in": 3,
      "out": 0
    },
    {
      "id": "persistence_history_historystore_cs_historystore",
      "name": "HistoryStore.GetModHistory",
      "file": "Persistence/History/HistoryStore.cs",
      "line": 45,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_history_historystore_cs_historystore",
      "name": "HistoryStore.WindowedStats",
      "file": "Persistence/History/HistoryStore.cs",
      "line": 90,
      "in": 0,
      "out": 3
    },
    {
      "id": "persistence_history_historystore_cs_historystore",
      "name": "HistoryStore.RecentSessions",
      "file": "Persistence/History/HistoryStore.cs",
      "line": 153,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_history_historystore_cs_historystore",
      "name": "HistoryStore.DataHealth",
      "file": "Persistence/History/HistoryStore.cs",
      "line": 176,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_history_rollupapplier_cs_rollupappli",
      "name": "RollupApplier.Apply",
      "file": "Persistence/History/RollupApplier.cs",
      "line": 22,
      "in": 0,
      "out": 3
    },
    {
      "id": "persistence_history_rollupbackfill_cs_rollupback",
      "name": "RollupBackfill.Run",
      "file": "Persistence/History/RollupBackfill.cs",
      "line": 43,
      "in": 0,
      "out": 3
    },
    {
      "id": "persistence_history_rollupbackfill_cs_rollupback",
      "name": "RollupBackfill.BuildFromSession",
      "file": "Persistence/History/RollupBackfill.cs",
      "line": 69,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_history_rollupfold_cs_rollupfold_alr",
      "name": "RollupFold.AlreadyFolded",
      "file": "Persistence/History/RollupFold.cs",
      "line": 44,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_history_rollupfold_cs_rollupfold_fol",
      "name": "RollupFold.FoldGlobal",
      "file": "Persistence/History/RollupFold.cs",
      "line": 58,
      "in": 1,
      "out": 3
    },
    {
      "id": "persistence_history_rollupfold_cs_rollupfold_fol",
      "name": "RollupFold.FoldModlist",
      "file": "Persistence/History/RollupFold.cs",
      "line": 110,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_history_rollupfold_cs_rollupfold_tri",
      "name": "RollupFold.TrimRing",
      "file": "Persistence/History/RollupFold.cs",
      "line": 136,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_interactions_interactionitem_cs_inte",
      "name": "InteractionItem.OnCreated",
      "file": "Persistence/Interactions/InteractionItem.cs",
      "line": 46,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_interactions_interactionitem_cs_inte",
      "name": "InteractionItem.OnSpawn",
      "file": "Persistence/Interactions/InteractionItem.cs",
      "line": 58,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_interactions_interactionitem_cs_inte",
      "name": "InteractionItem.OnPickup",
      "file": "Persistence/Interactions/InteractionItem.cs",
      "line": 70,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_interactions_interactionitem_cs_inte",
      "name": "InteractionItem.ResolveRecorder",
      "file": "Persistence/Interactions/InteractionItem.cs",
      "line": 84,
      "in": 3,
      "out": 0
    },
    {
      "id": "persistence_interactions_interactionitem_cs_inte",
      "name": "InteractionItem.Emit",
      "file": "Persistence/Interactions/InteractionItem.cs",
      "line": 90,
      "in": 4,
      "out": 6
    },
    {
      "id": "persistence_interactions_interactionnpc_cs_inter",
      "name": "InteractionNpc.OnSpawn",
      "file": "Persistence/Interactions/InteractionNpc.cs",
      "line": 30,
      "in": 0,
      "out": 7
    },
    {
      "id": "persistence_interactions_interactionnpc_cs_inter",
      "name": "InteractionNpc.OnKill",
      "file": "Persistence/Interactions/InteractionNpc.cs",
      "line": 70,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.OnHurt",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 97,
      "in": 0,
      "out": 5
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.OnHitNPC",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 124,
      "in": 0,
      "out": 5
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in_1",
      "name": "InteractionPlayer.OnHitNPCWithItem",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 144,
      "in": 0,
      "out": 5
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.OnHitNPCWithProj",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 163,
      "in": 0,
      "out": 5
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.PostUpdateBuffs",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 188,
      "in": 0,
      "out": 3
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.PostUpdateEquips",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 256,
      "in": 0,
      "out": 4
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.ComputeLoadoutHash",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 306,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.ComputeBuffHash",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 327,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.EmitBuffEdge",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 340,
      "in": 1,
      "out": 6
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.CaptureLoadout",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 367,
      "in": 1,
      "out": 4
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.SnapshotActiveBuffTypes",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 411,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.OtherIndexName",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 464,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.OwningModName",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 485,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_interactions_interactionplayer_cs_in",
      "name": "InteractionPlayer.ResolveRecorder",
      "file": "Persistence/Interactions/InteractionPlayer.cs",
      "line": 488,
      "in": 6,
      "out": 0
    },
    {
      "id": "persistence_lifecycle_modlistchange_cs_modlistch",
      "name": "ModlistChange()",
      "file": "Persistence/Lifecycle/ModlistChange.cs",
      "line": 25,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_lifecycle_modlistchange_cs_modlistch",
      "name": "ModlistChange.Diff",
      "file": "Persistence/Lifecycle/ModlistChange.cs",
      "line": 40,
      "in": 2,
      "out": 4
    },
    {
      "id": "persistence_lifecycle_storereset_cs_storereset_s",
      "name": "StoreReset.SessionScopedDeletes",
      "file": "Persistence/Lifecycle/StoreReset.cs",
      "line": 43,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_lifecycle_storereset_cs_storereset_e",
      "name": "StoreReset.Everything",
      "file": "Persistence/Lifecycle/StoreReset.cs",
      "line": 68,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_lifecycle_storereset_cs_storereset_r",
      "name": "StoreReset.RebuildRollup",
      "file": "Persistence/Lifecycle/StoreReset.cs",
      "line": 95,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_lifecycle_storereset_cs_storereset_f",
      "name": "StoreReset.ForgetModlist",
      "file": "Persistence/Lifecycle/StoreReset.cs",
      "line": 122,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_migrations_cs_migrations_apply_25",
      "name": "Migrations.Apply",
      "file": "Persistence/Migrations.cs",
      "line": 25,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_migrations_cs_migrations_step_43",
      "name": "Migrations.Step",
      "file": "Persistence/Migrations.cs",
      "line": 43,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_modlistfingerprint_cs_modlistfingerp",
      "name": "ModlistFingerprint.Compute",
      "file": "Persistence/ModlistFingerprint.cs",
      "line": 44,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_playerdeathdetector_cs_playerdeathde",
      "name": "PlayerDeathDetector.OnTick",
      "file": "Persistence/PlayerDeathDetector.cs",
      "line": 42,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_playerdeathdetector_cs_playerdeathde",
      "name": "PlayerDeathDetector.Capture",
      "file": "Persistence/PlayerDeathDetector.cs",
      "line": 72,
      "in": 1,
      "out": 6
    },
    {
      "id": "persistence_playerdeathdetector_cs_playerdeathde",
      "name": "PlayerDeathDetector.BuildSummary",
      "file": "Persistence/PlayerDeathDetector.cs",
      "line": 120,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_profilercompactcommand_cs_profilerco",
      "name": "ProfilerCompactCommand.Action",
      "file": "Persistence/ProfilerCompactCommand.cs",
      "line": 34,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase()",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 105,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase()",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 113,
      "in": 0,
      "out": 12
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.StartRollupBackfillIfNeeded",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 159,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.MarkRollupBackfillDone",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 184,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.ApplyBatch",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 204,
      "in": 2,
      "out": 2
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.DrainAndTruncateJournalForSessionEnd",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 240,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.Compact",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 265,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.DropAllUserData",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 279,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.RotateBackups",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 303,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.Dispose",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 325,
      "in": 0,
      "out": 3
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.RecoverIfNeeded",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 365,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.EnsureSchemaVersion",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 423,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.EnsureAllIndexes",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 454,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.PreWarmCollections",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 474,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.ReplayJournalIfNeeded",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 498,
      "in": 1,
      "out": 5
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.MarkCrashDetectedSessions",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 529,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.SweepExpiredWarmTier",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 544,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_profilerdatabase_cs_profilerdatabase",
      "name": "ProfilerDatabase.TouchMetadata",
      "file": "Persistence/ProfilerDatabase.cs",
      "line": 557,
      "in": 1,
      "out": 3
    },
    {
      "id": "persistence_profilerpaths_cs_profilerpaths_root_",
      "name": "ProfilerPaths.Root",
      "file": "Persistence/ProfilerPaths.cs",
      "line": 38,
      "in": 2,
      "out": 0
    },
    {
      "id": "persistence_profilerpaths_cs_profilerpaths_ensur",
      "name": "ProfilerPaths.EnsureDirectory",
      "file": "Persistence/ProfilerPaths.cs",
      "line": 48,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_records_buffeventrow_cs_buffeventrow",
      "name": "BuffEventRow.Reset",
      "file": "Persistence/Records/BuffEventRow.cs",
      "line": 43,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_records_damagedealtrow_cs_damagedeal",
      "name": "DamageDealtRow.Reset",
      "file": "Persistence/Records/DamageDealtRow.cs",
      "line": 56,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_records_damagetakenrow_cs_damagetake",
      "name": "DamageTakenRow.Reset",
      "file": "Persistence/Records/DamageTakenRow.cs",
      "line": 67,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_records_itemcreatedrow_cs_itemcreate",
      "name": "ItemCreatedRow.Reset",
      "file": "Persistence/Records/ItemCreatedRow.cs",
      "line": 60,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_records_loadoutsnapshotrow_cs_loadou",
      "name": "LoadoutSnapshotRow.Reset",
      "file": "Persistence/Records/LoadoutSnapshotRow.cs",
      "line": 51,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_records_npcspawnrow_cs_npcspawnrow_r",
      "name": "NpcSpawnRow.Reset",
      "file": "Persistence/Records/NpcSpawnRow.cs",
      "line": 51,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_records_welfordstat_cs_welfordstat_f",
      "name": "WelfordStat.FromStat",
      "file": "Persistence/Records/WelfordStat.cs",
      "line": 60,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_records_welfordstat_cs_welfordstat_f",
      "name": "WelfordStat.Fold",
      "file": "Persistence/Records/WelfordStat.cs",
      "line": 69,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_records_welfordstat_cs_welfordstat_f",
      "name": "WelfordStat.FoldSample",
      "file": "Persistence/Records/WelfordStat.cs",
      "line": 78,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_records_welfordstat_cs_welfordstat_f",
      "name": "WelfordStat.FoldSampleWeighted",
      "file": "Persistence/Records/WelfordStat.cs",
      "line": 93,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_report_htmlreportwriter_cs_htmlrepor",
      "name": "HtmlReportWriter.Render",
      "file": "Persistence/Report/HtmlReportWriter.cs",
      "line": 30,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_report_reportexporter_cs_reportexpor",
      "name": "ReportExporter.ExportSession",
      "file": "Persistence/Report/ReportExporter.cs",
      "line": 20,
      "in": 3,
      "out": 2
    },
    {
      "id": "persistence_report_sessionreport_cs_sessionrepor",
      "name": "SessionReportReader.Read",
      "file": "Persistence/Report/SessionReport.cs",
      "line": 67,
      "in": 5,
      "out": 2
    },
    {
      "id": "persistence_sessionsummarylogger_cs_sessionsumma",
      "name": "SessionSummaryLogger.Write",
      "file": "Persistence/SessionSummaryLogger.cs",
      "line": 34,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_streams_contexttransitionstream_cs_c",
      "name": "ContextTransitionStream.Apply",
      "file": "Persistence/Streams/ContextTransitionStream.cs",
      "line": 23,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_contexttransitionstream_cs_c",
      "name": "ContextTransitionStream.Reconstruct",
      "file": "Persistence/Streams/ContextTransitionStream.cs",
      "line": 28,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_streams_contexttransitionstream_cs_c",
      "name": "ContextTransitionStream.EnsureIndexes",
      "file": "Persistence/Streams/ContextTransitionStream.cs",
      "line": 33,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_streams_insightstream_cs_insightstre",
      "name": "InsightStream.Apply",
      "file": "Persistence/Streams/InsightStream.cs",
      "line": 23,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_insightstream_cs_insightstre",
      "name": "InsightStream.Reconstruct",
      "file": "Persistence/Streams/InsightStream.cs",
      "line": 28,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_insightstream_cs_insightstre",
      "name": "InsightStream.EnsureIndexes",
      "file": "Persistence/Streams/InsightStream.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_interactionstreams_cs_damage",
      "name": "DamageTakenStream.Apply",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 37,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_interactionstreams_cs_damage",
      "name": "DamageTakenStream.EnsureIndexes",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_interactionstreams_cs_damage",
      "name": "DamageDealtStream.Apply",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 56,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_interactionstreams_cs_damage",
      "name": "DamageDealtStream.EnsureIndexes",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 64,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_interactionstreams_cs_npcspa",
      "name": "NpcSpawnStream.Apply",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 76,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_interactionstreams_cs_npcspa",
      "name": "NpcSpawnStream.EnsureIndexes",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 84,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_interactionstreams_cs_itemcr",
      "name": "ItemCreatedStream.Apply",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 96,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_interactionstreams_cs_itemcr",
      "name": "ItemCreatedStream.EnsureIndexes",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 104,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_interactionstreams_cs_loadou",
      "name": "LoadoutSnapshotStream.Apply",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 115,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_interactionstreams_cs_loadou",
      "name": "LoadoutSnapshotStream.EnsureIndexes",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 123,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_interactionstreams_cs_buffev",
      "name": "BuffEventStream.Apply",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 134,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_interactionstreams_cs_buffev",
      "name": "BuffEventStream.EnsureIndexes",
      "file": "Persistence/Streams/InteractionStreams.cs",
      "line": 142,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_modliststream_cs_modliststre",
      "name": "ModlistStream.Apply",
      "file": "Persistence/Streams/ModlistStream.cs",
      "line": 33,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_modliststream_cs_modliststre",
      "name": "ModlistStream.Reconstruct",
      "file": "Persistence/Streams/ModlistStream.cs",
      "line": 107,
      "in": 0,
      "out": 3
    },
    {
      "id": "persistence_streams_modliststream_cs_modliststre",
      "name": "ModlistStream.EnsureIndexes",
      "file": "Persistence/Streams/ModlistStream.cs",
      "line": 121,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_persessionaggregatestream_cs",
      "name": "PerSessionAggregateStream.Apply",
      "file": "Persistence/Streams/PerSessionAggregateStream.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_persessionaggregatestream_cs",
      "name": "PerSessionAggregateStream.Reconstruct",
      "file": "Persistence/Streams/PerSessionAggregateStream.cs",
      "line": 57,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_streams_persessionaggregatestream_cs",
      "name": "PerSessionAggregateStream.EnsureIndexes",
      "file": "Persistence/Streams/PerSessionAggregateStream.cs",
      "line": 70,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_playerdeathstream_cs_playerd",
      "name": "PlayerDeathStream.Apply",
      "file": "Persistence/Streams/PlayerDeathStream.cs",
      "line": 23,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_playerdeathstream_cs_playerd",
      "name": "PlayerDeathStream.Reconstruct",
      "file": "Persistence/Streams/PlayerDeathStream.cs",
      "line": 28,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_playerdeathstream_cs_playerd",
      "name": "PlayerDeathStream.EnsureIndexes",
      "file": "Persistence/Streams/PlayerDeathStream.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_rollupstream_cs_rollupstream",
      "name": "RollupStream.Apply",
      "file": "Persistence/Streams/RollupStream.cs",
      "line": 37,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_rollupstream_cs_rollupstream",
      "name": "RollupStream.Reconstruct",
      "file": "Persistence/Streams/RollupStream.cs",
      "line": 40,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_rollupstream_cs_rollupstream",
      "name": "RollupStream.EnsureIndexes",
      "file": "Persistence/Streams/RollupStream.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_segmentstream_cs_segmentstre",
      "name": "SegmentStream.Apply",
      "file": "Persistence/Streams/SegmentStream.cs",
      "line": 28,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_segmentstream_cs_segmentstre",
      "name": "SegmentStream.Reconstruct",
      "file": "Persistence/Streams/SegmentStream.cs",
      "line": 33,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_segmentstream_cs_segmentstre",
      "name": "SegmentStream.EnsureIndexes",
      "file": "Persistence/Streams/SegmentStream.cs",
      "line": 38,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder()",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 109,
      "in": 1,
      "out": 3
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnTick",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 158,
      "in": 0,
      "out": 3
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnContextTransition",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 189,
      "in": 2,
      "out": 3
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnPlayerDeath",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 209,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnWorldSnapshot",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 221,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnDamageTaken",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 227,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.AggregateRecentDamage",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 252,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnDamageDealt",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 292,
      "in": 3,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnNpcSpawn",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 298,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnItemCreated",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 304,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnLoadoutSnapshot",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 310,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.OnBuffEvent",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 316,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.IsHeadline",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 323,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre_1",
      "name": "SessionRecorder.End",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 336,
      "in": 2,
      "out": 13
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.BuildRollupInput",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 379,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.DrainSpikes",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 419,
      "in": 2,
      "out": 4
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.DrainStalls",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 466,
      "in": 2,
      "out": 8
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.FlushCluster",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 562,
      "in": 2,
      "out": 3
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.AccumulateContribCost",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 604,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.AddContrib",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 610,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.BuildModAggregates",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 621,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.BuildTopHooks",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 689,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.BuildHookAggregates",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 711,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.BuildArchive",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 747,
      "in": 1,
      "out": 1
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre",
      "name": "SessionRecorder.BuildSpikeTopContributors",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 791,
      "in": 1,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionrecorder_cs_sessionre_2",
      "name": "SessionRecorder.ToList",
      "file": "Persistence/Streams/SessionRecorder.cs",
      "line": 824,
      "in": 9,
      "out": 1
    },
    {
      "id": "persistence_streams_sessionstream_cs_sessionstre",
      "name": "SessionStream.Apply",
      "file": "Persistence/Streams/SessionStream.cs",
      "line": 31,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_sessionstream_cs_sessionstre",
      "name": "SessionStream.Reconstruct",
      "file": "Persistence/Streams/SessionStream.cs",
      "line": 58,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_streams_sessionstream_cs_sessionstre",
      "name": "SessionStream.EnsureIndexes",
      "file": "Persistence/Streams/SessionStream.cs",
      "line": 74,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_spikestream_cs_spikestream_a",
      "name": "SpikeStream.Apply",
      "file": "Persistence/Streams/SpikeStream.cs",
      "line": 23,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_spikestream_cs_spikestream_r",
      "name": "SpikeStream.Reconstruct",
      "file": "Persistence/Streams/SpikeStream.cs",
      "line": 28,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_spikestream_cs_spikestream_e",
      "name": "SpikeStream.EnsureIndexes",
      "file": "Persistence/Streams/SpikeStream.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_stallclusterstream_cs_stallc",
      "name": "StallClusterStream.Apply",
      "file": "Persistence/Streams/StallClusterStream.cs",
      "line": 23,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_stallclusterstream_cs_stallc",
      "name": "StallClusterStream.Reconstruct",
      "file": "Persistence/Streams/StallClusterStream.cs",
      "line": 28,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_stallclusterstream_cs_stallc",
      "name": "StallClusterStream.EnsureIndexes",
      "file": "Persistence/Streams/StallClusterStream.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_stallstream_cs_stallstream_a",
      "name": "StallStream.Apply",
      "file": "Persistence/Streams/StallStream.cs",
      "line": 23,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_stallstream_cs_stallstream_r",
      "name": "StallStream.Reconstruct",
      "file": "Persistence/Streams/StallStream.cs",
      "line": 28,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_stallstream_cs_stallstream_e",
      "name": "StallStream.EnsureIndexes",
      "file": "Persistence/Streams/StallStream.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_streamjson_cs_streamjson_des",
      "name": "StreamJson.Deserialize",
      "file": "Persistence/Streams/StreamJson.cs",
      "line": 32,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_streamregistry_cs_streamregi",
      "name": "StreamRegistry()",
      "file": "Persistence/Streams/StreamRegistry.cs",
      "line": 34,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_streams_streamregistry_cs_streamregi",
      "name": "StreamRegistry.Lookup",
      "file": "Persistence/Streams/StreamRegistry.cs",
      "line": 57,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_streamregistry_cs_streamregi",
      "name": "StreamRegistry.Default",
      "file": "Persistence/Streams/StreamRegistry.cs",
      "line": 70,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_tickaggregatestream_cs_ticka",
      "name": "TickAggregateStream.Apply",
      "file": "Persistence/Streams/TickAggregateStream.cs",
      "line": 33,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_tickaggregatestream_cs_ticka",
      "name": "TickAggregateStream.Reconstruct",
      "file": "Persistence/Streams/TickAggregateStream.cs",
      "line": 66,
      "in": 0,
      "out": 3
    },
    {
      "id": "persistence_streams_tickaggregatestream_cs_ticka",
      "name": "TickAggregateStream.EnsureIndexes",
      "file": "Persistence/Streams/TickAggregateStream.cs",
      "line": 80,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_worldsnapshotstream_cs_world",
      "name": "WorldSnapshotStream.Apply",
      "file": "Persistence/Streams/WorldSnapshotStream.cs",
      "line": 23,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_streams_worldsnapshotstream_cs_world",
      "name": "WorldSnapshotStream.Reconstruct",
      "file": "Persistence/Streams/WorldSnapshotStream.cs",
      "line": 28,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_streams_worldsnapshotstream_cs_world",
      "name": "WorldSnapshotStream.EnsureIndexes",
      "file": "Persistence/Streams/WorldSnapshotStream.cs",
      "line": 33,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_tickdownsampler_cs_tickdownsampler_o",
      "name": "TickDownsampler.OnTickCommitted",
      "file": "Persistence/TickDownsampler.cs",
      "line": 49,
      "in": 1,
      "out": 3
    },
    {
      "id": "persistence_tickdownsampler_cs_tickdownsampler_e",
      "name": "TickDownsampler.EmitWarm",
      "file": "Persistence/TickDownsampler.cs",
      "line": 74,
      "in": 1,
      "out": 3
    },
    {
      "id": "persistence_tickdownsampler_cs_tickdownsampler_e",
      "name": "TickDownsampler.EmitCold",
      "file": "Persistence/TickDownsampler.cs",
      "line": 93,
      "in": 1,
      "out": 3
    },
    {
      "id": "persistence_tickdownsampler_cs_tickdownsampler_p",
      "name": "TickDownsampler.ProjectPerMod",
      "file": "Persistence/TickDownsampler.cs",
      "line": 117,
      "in": 2,
      "out": 1
    },
    {
      "id": "persistence_tickdownsampler_cs_rollingframe_ctor",
      "name": "RollingFrame()",
      "file": "Persistence/TickDownsampler.cs",
      "line": 153,
      "in": 0,
      "out": 0
    },
    {
      "id": "persistence_tickdownsampler_cs_rollingframe_reco",
      "name": "RollingFrame.RecomputeMax",
      "file": "Persistence/TickDownsampler.cs",
      "line": 166,
      "in": 1,
      "out": 0
    },
    {
      "id": "persistence_tickdownsampler_cs_rollingframe_push",
      "name": "RollingFrame.Push",
      "file": "Persistence/TickDownsampler.cs",
      "line": 189,
      "in": 0,
      "out": 1
    },
    {
      "id": "persistence_worldsnapshotter_cs_worldsnapshotter",
      "name": "WorldSnapshotter.OnTick",
      "file": "Persistence/WorldSnapshotter.cs",
      "line": 36,
      "in": 0,
      "out": 2
    },
    {
      "id": "persistence_worldsnapshotter_cs_worldsnapshotter",
      "name": "WorldSnapshotter.Capture",
      "file": "Persistence/WorldSnapshotter.cs",
      "line": 55,
      "in": 1,
      "out": 3
    },
    {
      "id": "profilerconfig_cs_profilerconfig_onchanged_145",
      "name": "ProfilerConfig.OnChanged",
      "file": "ProfilerConfig.cs",
      "line": 145,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_enumstringtable_cs_enumstringtable_cau",
      "name": "EnumStringTable.CauseName",
      "file": "Profiling/EnumStringTable.cs",
      "line": 64,
      "in": 3,
      "out": 0
    },
    {
      "id": "profiling_enumstringtable_cs_enumstringtable_sev",
      "name": "EnumStringTable.SeverityName",
      "file": "Profiling/EnumStringTable.cs",
      "line": 71,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_ctor",
      "name": "BiomeBitset()",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 34,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_resi",
      "name": "BiomeBitset.ResizeAndClear",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 44,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_isse",
      "name": "BiomeBitset.IsSet",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 51,
      "in": 3,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_set_",
      "name": "BiomeBitset.Set",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 57,
      "in": 5,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_clea",
      "name": "BiomeBitset.Clear",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 63,
      "in": 59,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_clea",
      "name": "BiomeBitset.ClearAll",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 70,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_word",
      "name": "BiomeBitset.WordAt",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 81,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_copy",
      "name": "BiomeBitset.CopyFrom",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 88,
      "in": 3,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_equa",
      "name": "BiomeBitset.Equals",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 98,
      "in": 7,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_geth",
      "name": "BiomeBitset.GetHashCode",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 108,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_prim",
      "name": "BiomeBitset.PrimaryBitIndex",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 117,
      "in": 3,
      "out": 0
    },
    {
      "id": "profiling_events_biomebitset_cs_biomebitset_popc",
      "name": "BiomeBitset.PopCount",
      "file": "Profiling/Events/BiomeBitset.cs",
      "line": 128,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_events_biomeregistry_cs_biomeregistry_",
      "name": "BiomeRegistry.Populate",
      "file": "Profiling/Events/BiomeRegistry.cs",
      "line": 73,
      "in": 1,
      "out": 3
    },
    {
      "id": "profiling_events_biomeregistry_cs_biomeregistry_",
      "name": "BiomeRegistry.Clear",
      "file": "Profiling/Events/BiomeRegistry.cs",
      "line": 135,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_events_biomeregistry_cs_biomeregistry_",
      "name": "BiomeRegistry.Sample",
      "file": "Profiling/Events/BiomeRegistry.cs",
      "line": 145,
      "in": 0,
      "out": 3
    },
    {
      "id": "profiling_events_biomeregistry_cs_biomeregistry_",
      "name": "BiomeRegistry.NameOrIndex",
      "file": "Profiling/Events/BiomeRegistry.cs",
      "line": 184,
      "in": 4,
      "out": 0
    },
    {
      "id": "profiling_events_biomeregistry_cs_biomeregistry_",
      "name": "BiomeRegistry.Humanise",
      "file": "Profiling/Events/BiomeRegistry.cs",
      "line": 191,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_events_bosssampler_cs_bosssampler_samp",
      "name": "BossSampler.Sample",
      "file": "Profiling/Events/BossSampler.cs",
      "line": 41,
      "in": 0,
      "out": 3
    },
    {
      "id": "profiling_events_bosssampler_cs_bosssampler_disp",
      "name": "BossSampler.DisplayName",
      "file": "Profiling/Events/BossSampler.cs",
      "line": 82,
      "in": 4,
      "out": 0
    },
    {
      "id": "profiling_events_bossslotarray_cs_bossslotarray_",
      "name": "BossSlotArray.Clear",
      "file": "Profiling/Events/BossSlotArray.cs",
      "line": 59,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_events_bossslotarray_cs_bossslotarray_",
      "name": "BossSlotArray.TryAdd",
      "file": "Profiling/Events/BossSlotArray.cs",
      "line": 66,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_events_bossslotarray_cs_bossslotarray_",
      "name": "BossSlotArray.Contains",
      "file": "Profiling/Events/BossSlotArray.cs",
      "line": 83,
      "in": 13,
      "out": 0
    },
    {
      "id": "profiling_events_bossslotarray_cs_bossslotarray_",
      "name": "BossSlotArray.Equals",
      "file": "Profiling/Events/BossSlotArray.cs",
      "line": 97,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_events_bucketstats_cs_bucketstats_add_",
      "name": "BucketStats.Add",
      "file": "Profiling/Events/BucketStats.cs",
      "line": 36,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_events_subworldprobe_cs_subworldprobe_",
      "name": "SubworldProbe.DisplayName",
      "file": "Profiling/Events/SubworldProbe.cs",
      "line": 46,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_events_subworldprobe_cs_subworldprobe_",
      "name": "SubworldProbe.Initialise",
      "file": "Profiling/Events/SubworldProbe.cs",
      "line": 57,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_events_subworldprobe_cs_subworldprobe_",
      "name": "SubworldProbe.Clear",
      "file": "Profiling/Events/SubworldProbe.cs",
      "line": 83,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_events_subworldprobe_cs_subworldprobe_",
      "name": "SubworldProbe.Sample",
      "file": "Profiling/Events/SubworldProbe.cs",
      "line": 90,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_events_weathersources_cs_weathersource",
      "name": "WeatherSources.readonly",
      "file": "Profiling/Events/WeatherSources.cs",
      "line": 30,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_events_weathersources_cs_weathersource",
      "name": "WeatherSources.DisplayName",
      "file": "Profiling/Events/WeatherSources.cs",
      "line": 47,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_hookcategoryrouter_cs_hookcategoryrout",
      "name": "HookCategoryRouter.ResolveCategory",
      "file": "Profiling/HookCategoryRouter.cs",
      "line": 43,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_ins",
      "name": "HookInterceptor.Install",
      "file": "Profiling/HookInterceptor.cs",
      "line": 282,
      "in": 1,
      "out": 4
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_ins",
      "name": "HookInterceptor.InstallForMod",
      "file": "Profiling/HookInterceptor.cs",
      "line": 359,
      "in": 1,
      "out": 3
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_hoo",
      "name": "HookInterceptor.HookSupportedOverrides",
      "file": "Profiling/HookInterceptor.cs",
      "line": 404,
      "in": 1,
      "out": 3
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_ish",
      "name": "HookInterceptor.IsHookOverride",
      "file": "Profiling/HookInterceptor.cs",
      "line": 452,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_rec",
      "name": "HookInterceptor.RecordUnsupported",
      "file": "Profiling/HookInterceptor.cs",
      "line": 458,
      "in": 1,
      "out": 3
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_sig",
      "name": "HookInterceptor.SignatureShape",
      "file": "Profiling/HookInterceptor.cs",
      "line": 479,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_try",
      "name": "HookInterceptor.TryHookSupportedOverride",
      "file": "Profiling/HookInterceptor.cs",
      "line": 503,
      "in": 1,
      "out": 3
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_cre",
      "name": "HookInterceptor.CreateProbe",
      "file": "Profiling/HookInterceptor.cs",
      "line": 780,
      "in": 1,
      "out": 3
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_dis",
      "name": "HookInterceptor.DisplayName",
      "file": "Profiling/HookInterceptor.cs",
      "line": 786,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_hookinterceptor_cs_hookinterceptor_log",
      "name": "HookInterceptor.LogSampleHookFailure",
      "file": "Profiling/HookInterceptor.cs",
      "line": 796,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_ctor_820",
      "name": "HookProbe()",
      "file": "Profiling/HookInterceptor.cs",
      "line": 820,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_time_828",
      "name": "HookProbe.Time",
      "file": "Profiling/HookInterceptor.cs",
      "line": 828,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timenpc_8",
      "name": "HookProbe.TimeNpc",
      "file": "Profiling/HookInterceptor.cs",
      "line": 845,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeproje",
      "name": "HookProbe.TimeProjectile",
      "file": "Profiling/HookInterceptor.cs",
      "line": 859,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timegamet",
      "name": "HookProbe.TimeGameTime",
      "file": "Profiling/HookInterceptor.cs",
      "line": 873,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeinter",
      "name": "HookProbe.TimeInterfaceLayers",
      "file": "Profiling/HookInterceptor.cs",
      "line": 887,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timesprit",
      "name": "HookProbe.TimeSpriteBatch",
      "file": "Profiling/HookInterceptor.cs",
      "line": 901,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timebool_",
      "name": "HookProbe.TimeBool",
      "file": "Profiling/HookInterceptor.cs",
      "line": 915,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timebooln",
      "name": "HookProbe.TimeBoolNpc",
      "file": "Profiling/HookInterceptor.cs",
      "line": 929,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeboolp",
      "name": "HookProbe.TimeBoolProjectile",
      "file": "Profiling/HookInterceptor.cs",
      "line": 943,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeboolp",
      "name": "HookProbe.TimeBoolPlayer",
      "file": "Profiling/HookInterceptor.cs",
      "line": 957,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timebooli",
      "name": "HookProbe.TimeBoolItem",
      "file": "Profiling/HookInterceptor.cs",
      "line": 971,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timevoidp",
      "name": "HookProbe.TimeVoidPlayer",
      "file": "Profiling/HookInterceptor.cs",
      "line": 985,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timevoidi",
      "name": "HookProbe.TimeVoidItem",
      "file": "Profiling/HookInterceptor.cs",
      "line": 999,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeitemp",
      "name": "HookProbe.TimeItemPlayer",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1013,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timebooli",
      "name": "HookProbe.TimeBoolItemPlayer",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1027,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timenpcpl",
      "name": "HookProbe.TimeNpcPlayer",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1041,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timebooln",
      "name": "HookProbe.TimeBoolNpcPlayer",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1055,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeproje",
      "name": "HookProbe.TimeProjectilePlayer",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1069,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeboolp",
      "name": "HookProbe.TimeBoolProjectilePlayer",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1083,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timenulla",
      "name": "HookProbe.TimeNullableBool",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1096,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeboolr",
      "name": "HookProbe.TimeBoolRefColor",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1109,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timegetal",
      "name": "HookProbe.TimeGetAlpha",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1122,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeint_1",
      "name": "HookProbe.TimeInt",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1135,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeplaye",
      "name": "HookProbe.TimePlayerBool",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1148,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timenpcre",
      "name": "HookProbe.TimeNpcRefHitModifiers",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1161,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timenpchi",
      "name": "HookProbe.TimeNpcHitInfoInt",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1174,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timetilei",
      "name": "HookProbe.TimeTileIntIntBoolRefInt",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1187,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timedrawi",
      "name": "HookProbe.TimeDrawItem",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1200,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hookinterceptor_cs_hookprobe_timeshoot",
      "name": "HookProbe.TimeShoot",
      "file": "Profiling/HookInterceptor.cs",
      "line": 1213,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_hooksurfacecache_cs_hooksurfacecache_g",
      "name": "HookSurfaceCache.GetTypes",
      "file": "Profiling/HookSurfaceCache.cs",
      "line": 58,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.Install",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 155,
      "in": 0,
      "out": 4
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.Uninstall",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 210,
      "in": 2,
      "out": 2
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.TrimRetainedScaffolding",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 260,
      "in": 1,
      "out": 1
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.InstallForMod",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 374,
      "in": 1,
      "out": 3
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.InstrumentTypeOverrides",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 422,
      "in": 1,
      "out": 6
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.IsHookOverride",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 551,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.DisplayName",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 557,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.LogSampleFailure",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 568,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.InstallTimingHook",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 588,
      "in": 1,
      "out": 1
    },
    {
      "id": "profiling_ilhookinterceptor_cs_ilhookinterceptor",
      "name": "ILHookInterceptor.ApplyTimingWrap",
      "file": "Profiling/ILHookInterceptor.cs",
      "line": 602,
      "in": 1,
      "out": 2
    },
    {
      "id": "profiling_langnamecache_cs_langnamecache_populat",
      "name": "LangNameCache.Populate",
      "file": "Profiling/LangNameCache.cs",
      "line": 54,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_langnamecache_cs_langnamecache_buff_10",
      "name": "LangNameCache.Buff",
      "file": "Profiling/LangNameCache.cs",
      "line": 104,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_langnamecache_cs_langnamecache_item_11",
      "name": "LangNameCache.Item",
      "file": "Profiling/LangNameCache.cs",
      "line": 111,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_langnamecache_cs_langnamecache_project",
      "name": "LangNameCache.Projectile",
      "file": "Profiling/LangNameCache.cs",
      "line": 118,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_langnamecache_cs_langnamecache_npc_125",
      "name": "LangNameCache.Npc",
      "file": "Profiling/LangNameCache.cs",
      "line": 125,
      "in": 4,
      "out": 0
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_cto",
      "name": "MetricCollector()",
      "file": "Profiling/MetricCollector.cs",
      "line": 192,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_cto",
      "name": "MetricCollector()",
      "file": "Profiling/MetricCollector.cs",
      "line": 197,
      "in": 0,
      "out": 2
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_beg",
      "name": "MetricCollector.BeginTick",
      "file": "Profiling/MetricCollector.cs",
      "line": 417,
      "in": 0,
      "out": 4
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_con",
      "name": "MetricCollector.ConfigureDetectorSensitivity",
      "file": "Profiling/MetricCollector.cs",
      "line": 473,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_ond",
      "name": "MetricCollector.OnDrawFrame",
      "file": "Profiling/MetricCollector.cs",
      "line": 530,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_end",
      "name": "MetricCollector.EndTick",
      "file": "Profiling/MetricCollector.cs",
      "line": 559,
      "in": 1,
      "out": 20
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_con",
      "name": "MetricCollector.ConsumeDivergenceLogTrigger",
      "file": "Profiling/MetricCollector.cs",
      "line": 768,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_sum",
      "name": "MetricCollector.SumAll",
      "file": "Profiling/MetricCollector.cs",
      "line": 785,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_upd",
      "name": "MetricCollector.UpdateRollingAverage",
      "file": "Profiling/MetricCollector.cs",
      "line": 805,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_tim",
      "name": "MetricCollector.TimestampDeltaMs",
      "file": "Profiling/MetricCollector.cs",
      "line": 849,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_metriccollector_cs_metriccollector_gcp",
      "name": "MetricCollector.GcPauseMilliseconds",
      "file": "Profiling/MetricCollector.cs",
      "line": 859,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_modownercache_cs_modownercache_foritem",
      "name": "ModOwnerCache.ForItem",
      "file": "Profiling/ModOwnerCache.cs",
      "line": 43,
      "in": 4,
      "out": 0
    },
    {
      "id": "profiling_modownercache_cs_modownercache_fornpc_",
      "name": "ModOwnerCache.ForNpc",
      "file": "Profiling/ModOwnerCache.cs",
      "line": 50,
      "in": 3,
      "out": 0
    },
    {
      "id": "profiling_modownercache_cs_modownercache_forproj",
      "name": "ModOwnerCache.ForProjectile",
      "file": "Profiling/ModOwnerCache.cs",
      "line": 57,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_modownercache_cs_modownercache_forbuff",
      "name": "ModOwnerCache.ForBuff",
      "file": "Profiling/ModOwnerCache.cs",
      "line": 64,
      "in": 2,
      "out": 0
    },
    {
      "id": "profiling_modownercache_cs_modownercache_froment",
      "name": "ModOwnerCache.FromEntitySource",
      "file": "Profiling/ModOwnerCache.cs",
      "line": 79,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_modramreader_cs_modram_ctor_39",
      "name": "ModRam()",
      "file": "Profiling/ModRamReader.cs",
      "line": 39,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_modramreader_cs_modramreader_resolve_5",
      "name": "ModRamReader.Resolve",
      "file": "Profiling/ModRamReader.cs",
      "line": 54,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_modramreader_cs_modramreader_tryread_1",
      "name": "ModRamReader.TryRead",
      "file": "Profiling/ModRamReader.cs",
      "line": 102,
      "in": 3,
      "out": 2
    },
    {
      "id": "profiling_pools_listpool_cs_listpool_rent_35",
      "name": "ListPool.Rent",
      "file": "Profiling/Pools/ListPool.cs",
      "line": 35,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_pools_listpool_cs_listpool_return_45",
      "name": "ListPool.Return",
      "file": "Profiling/Pools/ListPool.cs",
      "line": 45,
      "in": 0,
      "out": 2
    },
    {
      "id": "profiling_probestack_cs_probestack_enter_105",
      "name": "ProbeStack.Enter",
      "file": "Profiling/ProbeStack.cs",
      "line": 105,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_probestack_cs_probestack_takecallcount",
      "name": "ProbeStack.TakeCallCount",
      "file": "Profiling/ProbeStack.cs",
      "line": 136,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_probestack_cs_probestack_takedrawcallc",
      "name": "ProbeStack.TakeDrawCallCount",
      "file": "Profiling/ProbeStack.cs",
      "line": 147,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_probestack_cs_probestack_leave_162",
      "name": "ProbeStack.Leave",
      "file": "Profiling/ProbeStack.cs",
      "line": 162,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_probestack_cs_probestack_entercpualloc",
      "name": "ProbeStack.EnterCpuAlloc",
      "file": "Profiling/ProbeStack.cs",
      "line": 192,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_probestack_cs_probestack_leavecpualloc",
      "name": "ProbeStack.LeaveCpuAlloc",
      "file": "Profiling/ProbeStack.cs",
      "line": 224,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_profilerfocusprobe_cs_profilerfocuspro",
      "name": "ProfilerFocusProbe.Read",
      "file": "Profiling/ProfilerFocusProbe.cs",
      "line": 41,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth.RecordTickOverhead",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 203,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth()",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 213,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth.MarkInstallStart",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 226,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth.MarkInstallEnd",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 241,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth.Refresh",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 263,
      "in": 4,
      "out": 1
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth.RefreshIfStale",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 283,
      "in": 1,
      "out": 1
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth.SampleProcessState",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 290,
      "in": 2,
      "out": 6
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth.ClassifySeverity",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 336,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_profilerselfhealth_cs_profilerselfheal",
      "name": "ProfilerSelfHealth.RecordMemoryTrend",
      "file": "Profiling/ProfilerSelfHealth.cs",
      "line": 374,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_apply",
      "name": "ProfilerSystem.ApplyRuntimeConfig",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 58,
      "in": 2,
      "out": 1
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_posts",
      "name": "ProfilerSystem.PostSetupContent",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 152,
      "in": 0,
      "out": 7
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_onwor",
      "name": "ProfilerSystem.OnWorldLoad",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 291,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_runde",
      "name": "ProfilerSystem.RunDeferredWorldLoadInit",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 297,
      "in": 1,
      "out": 19
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_presa",
      "name": "ProfilerSystem.PreSaveAndQuit",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 477,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_kicko",
      "name": "ProfilerSystem.KickOffSessionEndAsync",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 499,
      "in": 2,
      "out": 14
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_onwor",
      "name": "ProfilerSystem.OnWorldUnload",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 679,
      "in": 0,
      "out": 5
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_preup",
      "name": "ProfilerSystem.PreUpdateEntities",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 729,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_postd",
      "name": "ProfilerSystem.PostDrawInterface",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 743,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_postu",
      "name": "ProfilerSystem.PostUpdateEverything",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 753,
      "in": 0,
      "out": 15
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_enque",
      "name": "ProfilerSystem.EnqueueModlistUpserts",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 939,
      "in": 1,
      "out": 4
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_count",
      "name": "ProfilerSystem.CountActive",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 987,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_count",
      "name": "ProfilerSystem.CountActive",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 1001,
      "in": 1,
      "out": 0
    },
    {
      "id": "profiling_profilersystem_cs_profilersystem_count",
      "name": "ProfilerSystem.CountActive",
      "file": "Profiling/ProfilerSystem.cs",
      "line": 1019,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_time_cs_time_reset_58",
      "name": "Time.Reset",
      "file": "Profiling/Time.cs",
      "line": 58,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_time_cs_time_unixmsnow_72",
      "name": "Time.UnixMsNow",
      "file": "Profiling/Time.cs",
      "line": 72,
      "in": 31,
      "out": 0
    },
    {
      "id": "profiling_util_boolindex_cs_boolindex_ctor_40",
      "name": "BoolIndex()",
      "file": "Profiling/Util/BoolIndex.cs",
      "line": 40,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_util_boolindex_cs_boolindex_contains_4",
      "name": "BoolIndex.Contains",
      "file": "Profiling/Util/BoolIndex.cs",
      "line": 47,
      "in": 0,
      "out": 0
    },
    {
      "id": "profiling_util_boolindex_cs_boolindex_add_53",
      "name": "BoolIndex.Add",
      "file": "Profiling/Util/BoolIndex.cs",
      "line": 53,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_util_boolindex_cs_boolindex_remove_59",
      "name": "BoolIndex.Remove",
      "file": "Profiling/Util/BoolIndex.cs",
      "line": 59,
      "in": 5,
      "out": 0
    },
    {
      "id": "profiling_util_boolindex_cs_boolindex_clear_64",
      "name": "BoolIndex.Clear",
      "file": "Profiling/Util/BoolIndex.cs",
      "line": 64,
      "in": 0,
      "out": 1
    },
    {
      "id": "profiling_util_boolindex_cs_boolindex_ensurecapa",
      "name": "BoolIndex.EnsureCapacity",
      "file": "Profiling/Util/BoolIndex.cs",
      "line": 69,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_components_donutchart_cs_donutchart_d",
      "name": "DonutChart.Draw",
      "file": "UI/Overlay/Components/DonutChart.cs",
      "line": 108,
      "in": 25,
      "out": 5
    },
    {
      "id": "ui_overlay_components_donutchart_cs_donutchart_d",
      "name": "DonutChart.Draw",
      "file": "UI/Overlay/Components/DonutChart.cs",
      "line": 169,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_components_donutchart_cs_donutchart_b",
      "name": "DonutChart.BuildRingTriangles",
      "file": "UI/Overlay/Components/DonutChart.cs",
      "line": 177,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_components_donutchart_cs_donutchart_e",
      "name": "DonutChart.EnsureVertexCapacity",
      "file": "UI/Overlay/Components/DonutChart.cs",
      "line": 221,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_components_donutchart_cs_donutchart_c",
      "name": "DonutChart.ComputeGeometryHash",
      "file": "UI/Overlay/Components/DonutChart.cs",
      "line": 235,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_components_donutchart_cs_donutchart_e",
      "name": "DonutChart.EnsureEffect",
      "file": "UI/Overlay/Components/DonutChart.cs",
      "line": 252,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_components_heatbar_cs_heatbar_draw_57",
      "name": "HeatBar.Draw",
      "file": "UI/Overlay/Components/HeatBar.cs",
      "line": 57,
      "in": 0,
      "out": 2
    },
    {
      "id": "ui_overlay_components_heatbar_cs_heatbar_drawsol",
      "name": "HeatBar.DrawSolid",
      "file": "UI/Overlay/Components/HeatBar.cs",
      "line": 82,
      "in": 0,
      "out": 1
    },
    {
      "id": "ui_overlay_components_impactskyline_cs_impactsky",
      "name": "ImpactSkyline.Draw",
      "file": "UI/Overlay/Components/ImpactSkyline.cs",
      "line": 62,
      "in": 0,
      "out": 2
    },
    {
      "id": "ui_overlay_components_nowplayingpanel_cs_nowplay",
      "name": "NowPlayingPanel.DrawFloating",
      "file": "UI/Overlay/Components/NowPlayingPanel.cs",
      "line": 60,
      "in": 0,
      "out": 8
    },
    {
      "id": "ui_overlay_components_nowplayingpanel_cs_nowplay",
      "name": "NowPlayingPanel.FamilyWeight",
      "file": "UI/Overlay/Components/NowPlayingPanel.cs",
      "line": 147,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_components_nowplayingpanel_cs_nowplay",
      "name": "NowPlayingPanel.FamilyColor",
      "file": "UI/Overlay/Components/NowPlayingPanel.cs",
      "line": 161,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_components_nowplayingpanel_cs_nowplay",
      "name": "NowPlayingPanel.Truncate",
      "file": "UI/Overlay/Components/NowPlayingPanel.cs",
      "line": 175,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_components_pill_cs_pill_draw_33",
      "name": "Pill.Draw",
      "file": "UI/Overlay/Components/Pill.cs",
      "line": 33,
      "in": 0,
      "out": 3
    },
    {
      "id": "ui_overlay_components_pill_cs_pill_hittest_55",
      "name": "Pill.HitTest",
      "file": "UI/Overlay/Components/Pill.cs",
      "line": 55,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_components_profilercard_cs_profilerca",
      "name": "ProfilerCard.Draw",
      "file": "UI/Overlay/Components/ProfilerCard.cs",
      "line": 56,
      "in": 0,
      "out": 3
    },
    {
      "id": "ui_overlay_components_retrospectivetoast_cs_retr",
      "name": "RetrospectiveToast.Pump",
      "file": "UI/Overlay/Components/RetrospectiveToast.cs",
      "line": 68,
      "in": 0,
      "out": 5
    },
    {
      "id": "ui_overlay_components_retrospectivetoast_cs_retr",
      "name": "RetrospectiveToast.Draw",
      "file": "UI/Overlay/Components/RetrospectiveToast.cs",
      "line": 113,
      "in": 0,
      "out": 5
    },
    {
      "id": "ui_overlay_components_retrospectivetoast_cs_retr",
      "name": "RetrospectiveToast.TryDismiss",
      "file": "UI/Overlay/Components/RetrospectiveToast.cs",
      "line": 155,
      "in": 0,
      "out": 1
    },
    {
      "id": "ui_overlay_components_retrospectivetoast_cs_retr",
      "name": "RetrospectiveToast.DrawRibbon",
      "file": "UI/Overlay/Components/RetrospectiveToast.cs",
      "line": 177,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_components_segmentcard_cs_segmentcard",
      "name": "SegmentCard.Draw",
      "file": "UI/Overlay/Components/SegmentCard.cs",
      "line": 44,
      "in": 0,
      "out": 4
    },
    {
      "id": "ui_overlay_components_segmentcard_cs_segmentcard",
      "name": "SegmentCard.BuildBadgeLine",
      "file": "UI/Overlay/Components/SegmentCard.cs",
      "line": 118,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_components_segmentcard_cs_segmentcard",
      "name": "SegmentCard.FormatDuration",
      "file": "UI/Overlay/Components/SegmentCard.cs",
      "line": 130,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_components_severitybadge_cs_severityb",
      "name": "SeverityBadge.DrawStall",
      "file": "UI/Overlay/Components/SeverityBadge.cs",
      "line": 41,
      "in": 0,
      "out": 1
    },
    {
      "id": "ui_overlay_components_severitybadge_cs_severityb",
      "name": "SeverityBadge.DrawConfidence",
      "file": "UI/Overlay/Components/SeverityBadge.cs",
      "line": 54,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_components_severitybadge_cs_severityb",
      "name": "SeverityBadge.DrawScope",
      "file": "UI/Overlay/Components/SeverityBadge.cs",
      "line": 68,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_components_severitybadge_cs_severityb",
      "name": "SeverityBadge.DrawSelfHealth",
      "file": "UI/Overlay/Components/SeverityBadge.cs",
      "line": 86,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_components_severitybadge_cs_severityb",
      "name": "SeverityBadge.Draw",
      "file": "UI/Overlay/Components/SeverityBadge.cs",
      "line": 98,
      "in": 4,
      "out": 3
    },
    {
      "id": "ui_overlay_components_severitybadge_cs_severityb",
      "name": "SeverityBadge.MeasureWidth",
      "file": "UI/Overlay/Components/SeverityBadge.cs",
      "line": 118,
      "in": 2,
      "out": 0
    },
    {
      "id": "ui_overlay_components_sparkline_cs_sparkline_dra",
      "name": "Sparkline.DrawFilledArea",
      "file": "UI/Overlay/Components/Sparkline.cs",
      "line": 54,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_components_sparkline_cs_sparkline_dra",
      "name": "Sparkline.DrawBars",
      "file": "UI/Overlay/Components/Sparkline.cs",
      "line": 88,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_components_sparkline_cs_sparkline_dra",
      "name": "Sparkline.DrawMarkers",
      "file": "UI/Overlay/Components/Sparkline.cs",
      "line": 114,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_components_statblock_cs_statblock_dra",
      "name": "StatBlock.Draw",
      "file": "UI/Overlay/Components/StatBlock.cs",
      "line": 51,
      "in": 0,
      "out": 1
    },
    {
      "id": "ui_overlay_components_timelinestrip_cs_timelines",
      "name": "TimelineStrip.Draw",
      "file": "UI/Overlay/Components/TimelineStrip.cs",
      "line": 56,
      "in": 0,
      "out": 1
    },
    {
      "id": "ui_overlay_overlaydraw_cs_overlaydraw_text_57",
      "name": "OverlayDraw.Text",
      "file": "UI/Overlay/OverlayDraw.cs",
      "line": 57,
      "in": 40,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaydraw_cs_overlaydraw_stat_77",
      "name": "OverlayDraw.Stat",
      "file": "UI/Overlay/OverlayDraw.cs",
      "line": 77,
      "in": 0,
      "out": 1
    },
    {
      "id": "ui_overlay_overlaydraw_cs_overlaydraw_bar_89",
      "name": "OverlayDraw.Bar",
      "file": "UI/Overlay/OverlayDraw.cs",
      "line": 89,
      "in": 3,
      "out": 2
    },
    {
      "id": "ui_overlay_overlaydraw_cs_overlaydraw_toggle_107",
      "name": "OverlayDraw.Toggle",
      "file": "UI/Overlay/OverlayDraw.cs",
      "line": 107,
      "in": 0,
      "out": 3
    },
    {
      "id": "ui_overlay_overlaydraw_cs_overlaydraw_truncate_1",
      "name": "OverlayDraw.Truncate",
      "file": "UI/Overlay/OverlayDraw.cs",
      "line": 117,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaydraw_cs_overlaydraw_formatbyte",
      "name": "OverlayDraw.FormatBytes",
      "file": "UI/Overlay/OverlayDraw.cs",
      "line": 125,
      "in": 2,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_initialh",
      "name": "OverlayPanel.InitialHeight",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 79,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_leftmous",
      "name": "OverlayPanel.LeftMouseDown",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 101,
      "in": 0,
      "out": 4
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_leftmous",
      "name": "OverlayPanel.LeftMouseUp",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 144,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_scrollwh",
      "name": "OverlayPanel.ScrollWheel",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 165,
      "in": 0,
      "out": 2
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_clickhea",
      "name": "OverlayPanel.ClickHeaderPill",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 175,
      "in": 1,
      "out": 3
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_rectcont",
      "name": "OverlayPanel.RectContainsLocal",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 211,
      "in": 2,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_clicktab",
      "name": "OverlayPanel.ClickTabStrip",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 214,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_update_2",
      "name": "OverlayPanel.Update",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 239,
      "in": 7,
      "out": 6
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_applyhei",
      "name": "OverlayPanel.ApplyHeight",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 275,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_applywid",
      "name": "OverlayPanel.ApplyWidth",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 285,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_followmo",
      "name": "OverlayPanel.FollowMouse",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 295,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawself",
      "name": "OverlayPanel.DrawSelf",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 307,
      "in": 0,
      "out": 12
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawresi",
      "name": "OverlayPanel.DrawResizeHandle",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 343,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawhead",
      "name": "OverlayPanel.DrawHeader",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 360,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawtabs",
      "name": "OverlayPanel.DrawTabStrip",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 411,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawstat",
      "name": "OverlayPanel.DrawStatCards",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 453,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawempt",
      "name": "OverlayPanel.DrawEmptyStatCards",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 476,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawstat",
      "name": "OverlayPanel.DrawStatCard",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 485,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_layoutst",
      "name": "OverlayPanel.LayoutStatCards",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 499,
      "in": 2,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawheal",
      "name": "OverlayPanel.DrawHealthCard",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 522,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawempt",
      "name": "OverlayPanel.DrawEmptyHealthCard",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 535,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_layouthe",
      "name": "OverlayPanel.LayoutHealthCard",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 541,
      "in": 2,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_drawheal",
      "name": "OverlayPanel.DrawHealthBody",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 554,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_coverage",
      "name": "OverlayPanel.CoverageTotals",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 639,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_averagef",
      "name": "OverlayPanel.AverageFrameTimeMs",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 656,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaypanel_cs_overlaypanel_colourfo",
      "name": "OverlayPanel.ColourForFrameMs",
      "file": "UI/Overlay/OverlayPanel.cs",
      "line": 669,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaystate_cs_overlaystate_selected",
      "name": "OverlayState.SelectedCategoryMs",
      "file": "UI/Overlay/OverlayState.cs",
      "line": 84,
      "in": 4,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaystate_cs_overlaystate_selected",
      "name": "OverlayState.SelectedHookMs",
      "file": "UI/Overlay/OverlayState.cs",
      "line": 90,
      "in": 3,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaystate_cs_overlaystate_selected",
      "name": "OverlayState.SelectedCategoryBytes",
      "file": "UI/Overlay/OverlayState.cs",
      "line": 99,
      "in": 2,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaystate_cs_overlaystate_selected",
      "name": "OverlayState.SelectedHookBytes",
      "file": "UI/Overlay/OverlayState.cs",
      "line": 107,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_overlaystate_cs_overlaystate_captures",
      "name": "OverlayState.CaptureSnapshot",
      "file": "UI/Overlay/OverlayState.cs",
      "line": 119,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabregistry_cs_tabregistry_visible_73",
      "name": "TabRegistry.Visible",
      "file": "UI/Overlay/TabRegistry.cs",
      "line": 73,
      "in": 3,
      "out": 3
    },
    {
      "id": "ui_overlay_tabregistry_cs_tabregistry_resolveact",
      "name": "TabRegistry.ResolveActive",
      "file": "UI/Overlay/TabRegistry.cs",
      "line": 96,
      "in": 4,
      "out": 1
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventrow_ctor_44",
      "name": "EventRow()",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 44,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_isavailab",
      "name": "EventsTab.IsAvailable",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 102,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_tick_108",
      "name": "EventsTab.Tick",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 108,
      "in": 0,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_measurepa",
      "name": "EventsTab.MeasurePanelHeight",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 134,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_handlecli",
      "name": "EventsTab.HandleClick",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 149,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_handlescr",
      "name": "EventsTab.HandleScroll",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 155,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_buildrows",
      "name": "EventsTab.BuildRows",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 163,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_rowordering_compare",
      "name": "RowOrdering.Compare",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 194,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_dimension",
      "name": "EventsTab.DimensionLabel",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 202,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_draw_215",
      "name": "EventsTab.Draw",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 215,
      "in": 0,
      "out": 5
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_drawcolum",
      "name": "EventsTab.DrawColumnHeader",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 269,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_drawrow_2",
      "name": "EventsTab.DrawRow",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 280,
      "in": 1,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_drawfoote",
      "name": "EventsTab.DrawFooter",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 320,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_formatdwe",
      "name": "EventsTab.FormatDwell",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 328,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_computeno",
      "name": "EventsTab.ComputeNowActiveSummary",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 343,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_tabs_eventstab_cs_eventstab_invasions",
      "name": "EventsTab.InvasionShortName",
      "file": "UI/Overlay/Tabs/EventsTab.cs",
      "line": 381,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_insightstab_cs_insightstab_isava",
      "name": "InsightsTab.IsAvailable",
      "file": "UI/Overlay/Tabs/InsightsTab.cs",
      "line": 61,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_insightstab_cs_insightstab_tick_",
      "name": "InsightsTab.Tick",
      "file": "UI/Overlay/Tabs/InsightsTab.cs",
      "line": 63,
      "in": 0,
      "out": 6
    },
    {
      "id": "ui_overlay_tabs_insightstab_cs_insightstab_measu",
      "name": "InsightsTab.MeasurePanelHeight",
      "file": "UI/Overlay/Tabs/InsightsTab.cs",
      "line": 88,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_insightstab_cs_insightstab_handl",
      "name": "InsightsTab.HandleClick",
      "file": "UI/Overlay/Tabs/InsightsTab.cs",
      "line": 96,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_insightstab_cs_insightstab_handl",
      "name": "InsightsTab.HandleScroll",
      "file": "UI/Overlay/Tabs/InsightsTab.cs",
      "line": 97,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_insightstab_cs_insightstab_draw_",
      "name": "InsightsTab.Draw",
      "file": "UI/Overlay/Tabs/InsightsTab.cs",
      "line": 99,
      "in": 0,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_insightstab_cs_insightstab_drawi",
      "name": "InsightsTab.DrawInsightCard",
      "file": "UI/Overlay/Tabs/InsightsTab.cs",
      "line": 139,
      "in": 1,
      "out": 6
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_reado",
      "name": "OverviewTab.readonly",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 97,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_isava",
      "name": "OverviewTab.IsAvailable",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 105,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_tick_",
      "name": "OverviewTab.Tick",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 107,
      "in": 0,
      "out": 5
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_measu",
      "name": "OverviewTab.MeasurePanelHeight",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 123,
      "in": 0,
      "out": 4
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_handl",
      "name": "OverviewTab.HandleScroll",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 150,
      "in": 0,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_handl",
      "name": "OverviewTab.HandleClick",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 157,
      "in": 0,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_draw_",
      "name": "OverviewTab.Draw",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 200,
      "in": 0,
      "out": 8
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_drawd",
      "name": "OverviewTab.DrawDonutCard",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 245,
      "in": 1,
      "out": 5
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_draws",
      "name": "OverviewTab.DrawSliceLegend",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 321,
      "in": 1,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_drawl",
      "name": "OverviewTab.DrawLegendDot",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 363,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_drawc",
      "name": "OverviewTab.DrawContributorsCard",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 372,
      "in": 1,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_drawc",
      "name": "OverviewTab.DrawContributorRow",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 396,
      "in": 1,
      "out": 7
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_compo",
      "name": "OverviewTab.ComposeComponentLine",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 436,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_draws",
      "name": "OverviewTab.DrawSparklineCard",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 446,
      "in": 1,
      "out": 6
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_draws",
      "name": "OverviewTab.DrawSortChips",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 483,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_drawa",
      "name": "OverviewTab.DrawAllModsRanking",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 509,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_drawa",
      "name": "OverviewTab.DrawAllModsRow",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 545,
      "in": 1,
      "out": 7
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_compo",
      "name": "OverviewTab.ComposeShortComponents",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 583,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_refre",
      "name": "OverviewTab.RefreshSlices",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 597,
      "in": 1,
      "out": 4
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_domin",
      "name": "OverviewTab.DominantHueFor",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 663,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_refre",
      "name": "OverviewTab.RefreshSparklines",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 670,
      "in": 1,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_ensur",
      "name": "OverviewTab.EnsureTruncatedNames",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 727,
      "in": 1,
      "out": 1
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_count",
      "name": "OverviewTab.CountVisibleRows",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 739,
      "in": 3,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_rowsp",
      "name": "OverviewTab.RowsPerView",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 748,
      "in": 4,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_donut",
      "name": "OverviewTab.DonutHForMode",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 752,
      "in": 3,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_spark",
      "name": "OverviewTab.SparklineRowHForMode",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 756,
      "in": 4,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_compu",
      "name": "OverviewTab.ComputeSeverityFraction",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 764,
      "in": 3,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_colou",
      "name": "OverviewTab.ColourForBand",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 773,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_overviewtab_cs_overviewtab_compu",
      "name": "OverviewTab.ComputeLowImpactCutoff",
      "file": "UI/Overlay/Tabs/OverviewTab.cs",
      "line": 776,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_selftab_cs_selftab_isavailable_4",
      "name": "SelfTab.IsAvailable",
      "file": "UI/Overlay/Tabs/SelfTab.cs",
      "line": 44,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_selftab_cs_selftab_tick_46",
      "name": "SelfTab.Tick",
      "file": "UI/Overlay/Tabs/SelfTab.cs",
      "line": 46,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_selftab_cs_selftab_measurepanelh",
      "name": "SelfTab.MeasurePanelHeight",
      "file": "UI/Overlay/Tabs/SelfTab.cs",
      "line": 48,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_selftab_cs_selftab_handleclick_5",
      "name": "SelfTab.HandleClick",
      "file": "UI/Overlay/Tabs/SelfTab.cs",
      "line": 54,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_selftab_cs_selftab_handlescroll_",
      "name": "SelfTab.HandleScroll",
      "file": "UI/Overlay/Tabs/SelfTab.cs",
      "line": 55,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_selftab_cs_selftab_draw_57",
      "name": "SelfTab.Draw",
      "file": "UI/Overlay/Tabs/SelfTab.cs",
      "line": 57,
      "in": 0,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_selftab_cs_selftab_drawinstallfo",
      "name": "SelfTab.DrawInstallFootprint",
      "file": "UI/Overlay/Tabs/SelfTab.cs",
      "line": 79,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_selftab_cs_selftab_drawprocessco",
      "name": "SelfTab.DrawProcessContext",
      "file": "UI/Overlay/Tabs/SelfTab.cs",
      "line": 120,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_isavailab",
      "name": "SpikesTab.IsAvailable",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 74,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_tick_76",
      "name": "SpikesTab.Tick",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 76,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_measurepa",
      "name": "SpikesTab.MeasurePanelHeight",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 83,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_handlecli",
      "name": "SpikesTab.HandleClick",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 102,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_handlescr",
      "name": "SpikesTab.HandleScroll",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 107,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_draw_114",
      "name": "SpikesTab.Draw",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 114,
      "in": 0,
      "out": 5
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_rebuildti",
      "name": "SpikesTab.RebuildTimelineMarks",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 195,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_drawspike",
      "name": "SpikesTab.DrawSpikeRow",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 257,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_drawstall",
      "name": "SpikesTab.DrawStallRow",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 291,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_stalldeta",
      "name": "SpikesTab.StallDetailLine",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 331,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_spikestab_cs_spikestab_topcontri",
      "name": "SpikesTab.TopContributorLabel",
      "file": "UI/Overlay/Tabs/SpikesTab.cs",
      "line": 355,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_isava",
      "name": "TimelineTab.IsAvailable",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 51,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_tick_",
      "name": "TimelineTab.Tick",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 53,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_measu",
      "name": "TimelineTab.MeasurePanelHeight",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 55,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_handl",
      "name": "TimelineTab.HandleClick",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 61,
      "in": 0,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_handl",
      "name": "TimelineTab.HandleScroll",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 90,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_draw_",
      "name": "TimelineTab.Draw",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 95,
      "in": 0,
      "out": 7
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_drawf",
      "name": "TimelineTab.DrawFilterStrip",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 161,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_drawr",
      "name": "TimelineTab.DrawRow",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 171,
      "in": 1,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_apply",
      "name": "TimelineTab.ApplyFilter",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 229,
      "in": 2,
      "out": 1
    },
    {
      "id": "ui_overlay_tabs_timelinetab_cs_timelinetab_cycle",
      "name": "TimelineTab.CycleFilter",
      "file": "UI/Overlay/Tabs/TimelineTab.cs",
      "line": 239,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_modrow_ctor_33",
      "name": "ModRow()",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 33,
      "in": 1,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_isavailable_6",
      "name": "TreeTab.IsAvailable",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 61,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_tick_63",
      "name": "TreeTab.Tick",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 63,
      "in": 0,
      "out": 4
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_measurepanelh",
      "name": "TreeTab.MeasurePanelHeight",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 98,
      "in": 0,
      "out": 5
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_handleclick_1",
      "name": "TreeTab.HandleClick",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 129,
      "in": 0,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_handlescroll_",
      "name": "TreeTab.HandleScroll",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 145,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_draw_153",
      "name": "TreeTab.Draw",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 153,
      "in": 0,
      "out": 10
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_drawmodrow_25",
      "name": "TreeTab.DrawModRow",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 253,
      "in": 1,
      "out": 6
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_drawcategoryr",
      "name": "TreeTab.DrawCategoryRow",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 276,
      "in": 1,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_drawhothookro",
      "name": "TreeTab.DrawHotHookRows",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 298,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_drawhookrow_3",
      "name": "TreeTab.DrawHookRow",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 322,
      "in": 1,
      "out": 3
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_hittestrows_3",
      "name": "TreeTab.HitTestRows",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 331,
      "in": 1,
      "out": 5
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_sortvisibleca",
      "name": "TreeTab.SortVisibleCategories",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 365,
      "in": 3,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_countvisibleh",
      "name": "TreeTab.CountVisibleHooks",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 398,
      "in": 2,
      "out": 1
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_findtophooks_",
      "name": "TreeTab.FindTopHooks",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 408,
      "in": 2,
      "out": 0
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_buildsortedro",
      "name": "TreeTab.BuildSortedRows",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 433,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_coveragebadge",
      "name": "TreeTab.CoverageBadge",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 458,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_overlay_tabs_treetab_cs_treetab_coveragecolor",
      "name": "TreeTab.CoverageColor",
      "file": "UI/Overlay/Tabs/TreeTab.cs",
      "line": 465,
      "in": 1,
      "out": 2
    },
    {
      "id": "ui_profileroverlay_cs_profileroverlay_oninitiali",
      "name": "ProfilerOverlay.OnInitialize",
      "file": "UI/ProfilerOverlay.cs",
      "line": 38,
      "in": 0,
      "out": 2
    },
    {
      "id": "ui_profileroverlaysystem_cs_profileroverlaysyste",
      "name": "ProfilerOverlaySystem.OnModLoad",
      "file": "UI/ProfilerOverlaySystem.cs",
      "line": 40,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_profileroverlaysystem_cs_profileroverlaysyste",
      "name": "ProfilerOverlaySystem.OnModUnload",
      "file": "UI/ProfilerOverlaySystem.cs",
      "line": 45,
      "in": 0,
      "out": 0
    },
    {
      "id": "ui_profilertheme_cs_profilertheme_modcolor_134",
      "name": "ProfilerTheme.ModColor",
      "file": "UI/ProfilerTheme.cs",
      "line": 134,
      "in": 3,
      "out": 0
    },
    {
      "id": "ui_profilertheme_cs_profilertheme_costcolor_160",
      "name": "ProfilerTheme.CostColor",
      "file": "UI/ProfilerTheme.cs",
      "line": 160,
      "in": 3,
      "out": 0
    },
    {
      "id": "ui_profilertheme_cs_profilertheme_fillrect_172",
      "name": "ProfilerTheme.FillRect",
      "file": "UI/ProfilerTheme.cs",
      "line": 172,
      "in": 27,
      "out": 1
    },
    {
      "id": "ui_profilertheme_cs_profilertheme_drawborder_178",
      "name": "ProfilerTheme.DrawBorder",
      "file": "UI/ProfilerTheme.cs",
      "line": 178,
      "in": 9,
      "out": 1
    },
    {
      "id": "ui_profilertheme_cs_profilertheme_drawpanel_188",
      "name": "ProfilerTheme.DrawPanel",
      "file": "UI/ProfilerTheme.cs",
      "line": 188,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_history_cs_dashboardrouter_b",
      "name": "DashboardRouter.BuildHistory",
      "file": "Web/DashboardRouter.History.cs",
      "line": 22,
      "in": 1,
      "out": 4
    },
    {
      "id": "web_dashboardrouter_history_cs_dashboardrouter_b",
      "name": "DashboardRouter.BuildDataHealth",
      "file": "Web/DashboardRouter.History.cs",
      "line": 95,
      "in": 1,
      "out": 5
    },
    {
      "id": "web_dashboardrouter_history_cs_dashboardrouter_p",
      "name": "DashboardRouter.ParseQueryValueRaw",
      "file": "Web/DashboardRouter.History.cs",
      "line": 153,
      "in": 1,
      "out": 0
    },
    {
      "id": "web_dashboardrouter_hooks_cs_dashboardrouter_bui",
      "name": "DashboardRouter.BuildHooks",
      "file": "Web/DashboardRouter.Hooks.cs",
      "line": 19,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_insights_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildInsights",
      "file": "Web/DashboardRouter.Insights.cs",
      "line": 18,
      "in": 1,
      "out": 3
    },
    {
      "id": "web_dashboardrouter_insights_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildModObservatory",
      "file": "Web/DashboardRouter.Insights.cs",
      "line": 104,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_insights_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildDormantSurface",
      "file": "Web/DashboardRouter.Insights.cs",
      "line": 191,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_insights_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildCrossCutting",
      "file": "Web/DashboardRouter.Insights.cs",
      "line": 226,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_insights_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildEngagementCost",
      "file": "Web/DashboardRouter.Insights.cs",
      "line": 267,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_insights_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildModInteraction",
      "file": "Web/DashboardRouter.Insights.cs",
      "line": 298,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_lag_cs_dashboardrouter_build",
      "name": "DashboardRouter.BuildSpikes",
      "file": "Web/DashboardRouter.Lag.cs",
      "line": 17,
      "in": 1,
      "out": 3
    },
    {
      "id": "web_dashboardrouter_lag_cs_dashboardrouter_build",
      "name": "DashboardRouter.BuildStalls",
      "file": "Web/DashboardRouter.Lag.cs",
      "line": 57,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_lag_cs_dashboardrouter_build",
      "name": "DashboardRouter.BuildLagClusters",
      "file": "Web/DashboardRouter.Lag.cs",
      "line": 98,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_lag_cs_dashboardrouter_build",
      "name": "DashboardRouter.BuildGcPressure",
      "file": "Web/DashboardRouter.Lag.cs",
      "line": 156,
      "in": 1,
      "out": 1
    },
    {
      "id": "web_dashboardrouter_lag_cs_dashboardrouter_build",
      "name": "DashboardRouter.BuildSegmentLagDensity",
      "file": "Web/DashboardRouter.Lag.cs",
      "line": 194,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_lag_cs_dashboardrouter_build",
      "name": "DashboardRouter.BuildAllocCausality",
      "file": "Web/DashboardRouter.Lag.cs",
      "line": 237,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_lag_cs_dashboardrouter_build",
      "name": "DashboardRouter.BuildLagRhythm",
      "file": "Web/DashboardRouter.Lag.cs",
      "line": 289,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_memory_cs_dashboardrouter_bu",
      "name": "DashboardRouter.BuildMemory",
      "file": "Web/DashboardRouter.Memory.cs",
      "line": 30,
      "in": 1,
      "out": 3
    },
    {
      "id": "web_dashboardrouter_modlists_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildModlistHistory",
      "file": "Web/DashboardRouter.Modlists.cs",
      "line": 23,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_mods_cs_dashboardrouter_buil",
      "name": "DashboardRouter.BuildMods",
      "file": "Web/DashboardRouter.Mods.cs",
      "line": 17,
      "in": 1,
      "out": 3
    },
    {
      "id": "web_dashboardrouter_report_cs_dashboardrouter_bu",
      "name": "DashboardRouter.BuildExportReport",
      "file": "Web/DashboardRouter.Report.cs",
      "line": 19,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_reset_cs_dashboardrouter_bui",
      "name": "DashboardRouter.BuildReset",
      "file": "Web/DashboardRouter.Reset.cs",
      "line": 21,
      "in": 1,
      "out": 5
    },
    {
      "id": "web_dashboardrouter_reset_cs_dashboardrouter_par",
      "name": "DashboardRouter.ParseQueryValue",
      "file": "Web/DashboardRouter.Reset.cs",
      "line": 65,
      "in": 1,
      "out": 0
    },
    {
      "id": "web_dashboardrouter_self_cs_dashboardrouter_buil",
      "name": "DashboardRouter.BuildSelf",
      "file": "Web/DashboardRouter.Self.cs",
      "line": 12,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_self_cs_dashboardrouter_buil",
      "name": "DashboardRouter.BuildMemoryGuard",
      "file": "Web/DashboardRouter.Self.cs",
      "line": 50,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_summary_cs_dashboardrouter_b",
      "name": "DashboardRouter.BuildNow",
      "file": "Web/DashboardRouter.Summary.cs",
      "line": 18,
      "in": 1,
      "out": 4
    },
    {
      "id": "web_dashboardrouter_summary_cs_dashboardrouter_b",
      "name": "DashboardRouter.BuildFrames",
      "file": "Web/DashboardRouter.Summary.cs",
      "line": 195,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_summary_cs_dashboardrouter_b",
      "name": "DashboardRouter.BuildHeatmap",
      "file": "Web/DashboardRouter.Summary.cs",
      "line": 269,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_summary_cs_dashboardrouter_a",
      "name": "DashboardRouter.AverageRecent",
      "file": "Web/DashboardRouter.Summary.cs",
      "line": 315,
      "in": 1,
      "out": 0
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildSegments",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 17,
      "in": 1,
      "out": 3
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildEvents",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 109,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildSegmentLifetime",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 141,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildSegmentModAttribution",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 179,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildTransitions",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 224,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildActivityStrip",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 255,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildAttendance",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 287,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildDeaths",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 322,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_timeline_cs_dashboardrouter_",
      "name": "DashboardRouter.BuildChronicle",
      "file": "Web/DashboardRouter.Timeline.cs",
      "line": 374,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_dashboardrouter_cs_dashboardrouter_route_42",
      "name": "DashboardRouter.Route",
      "file": "Web/DashboardRouter.cs",
      "line": 42,
      "in": 0,
      "out": 38
    },
    {
      "id": "web_dashboardrouter_cs_dashboardrouter_topcontri",
      "name": "DashboardRouter.TopContributors",
      "file": "Web/DashboardRouter.cs",
      "line": 95,
      "in": 1,
      "out": 2
    },
    {
      "id": "web_server_dashboardhttpserver_cs_dashboardhttps",
      "name": "DashboardHttpServer()",
      "file": "Web/Server/DashboardHttpServer.cs",
      "line": 79,
      "in": 1,
      "out": 0
    },
    {
      "id": "web_server_dashboardhttpserver_cs_dashboardhttps",
      "name": "DashboardHttpServer.Dispose",
      "file": "Web/Server/DashboardHttpServer.cs",
      "line": 91,
      "in": 0,
      "out": 0
    },
    {
      "id": "web_server_httprequest_cs_httprequest_ctor_26",
      "name": "HttpRequest()",
      "file": "Web/Server/HttpRequest.cs",
      "line": 26,
      "in": 0,
      "out": 0
    },
    {
      "id": "web_server_httpresponse_cs_httpresponse_ctor_25",
      "name": "HttpResponse()",
      "file": "Web/Server/HttpResponse.cs",
      "line": 25,
      "in": 4,
      "out": 0
    },
    {
      "id": "web_server_httpresponse_cs_httpresponse_html_32",
      "name": "HttpResponse.Html",
      "file": "Web/Server/HttpResponse.cs",
      "line": 32,
      "in": 1,
      "out": 1
    },
    {
      "id": "web_server_httpresponse_cs_httpresponse_json_35",
      "name": "HttpResponse.Json",
      "file": "Web/Server/HttpResponse.cs",
      "line": 35,
      "in": 1,
      "out": 1
    },
    {
      "id": "web_server_httpresponse_cs_httpresponse_plaintex",
      "name": "HttpResponse.PlainText",
      "file": "Web/Server/HttpResponse.cs",
      "line": 38,
      "in": 1,
      "out": 1
    }
  ]
}`);
