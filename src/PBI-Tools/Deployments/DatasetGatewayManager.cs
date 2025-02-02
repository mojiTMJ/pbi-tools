// Copyright (c) Mathias Thierbach
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.PowerBI.Api;
using Microsoft.PowerBI.Api.Models;
using Serilog;
using Spectre.Console;

namespace PbiTools.Deployments
{
    using Options = PbiDeploymentOptions.DatasetOptions.GatewayOptions;

    public class DatasetGatewayManager
    { 
        private static readonly ILogger Log = Serilog.Log.ForContext<DatasetGatewayManager>();

        private readonly Options _options;
        private readonly IPowerBIClient _powerBI;

        public DatasetGatewayManager(Options options, IPowerBIClient powerBIClient)
        {
            _options = options;
            _powerBI = powerBIClient ?? throw new ArgumentNullException(nameof(powerBIClient));

            Enabled = options != null && (options.GatewayId != default || options.DiscoverGateways);
        }

        public bool WhatIf { get; set; }

        public bool Enabled { get; }

        /// <summary>
        /// TODO
        /// </summary>
        public async Task DiscoverGatewaysAsync(Guid workspaceId, string datasetId)
        {
            if (!Enabled || !_options.DiscoverGateways || datasetId == default) return;

            var gateways = await _powerBI.Datasets.DiscoverGatewaysInGroupAsync(workspaceId, datasetId);

            var table = new Spectre.Console.Table { Width = Environment.UserInteractive ? null : 80 };

            table.AddColumns("ID", "Name", "Type");

            foreach (var item in gateways.Value)
            {
                table.AddRow(
                    item.Id.ToString(),
                    item.Name,
                    item.Type.ToString()
                );
            }

            Log.Information("Discovered Gateways:");

            AnsiConsole.Write(table);
        }

        /// <summary>
        /// TODO
        /// </summary>
        public async Task BindToGatewayAsync(Guid workspaceId, string datasetId, IDictionary<string, DeploymentParameter> parameters)
        {
            if (!Enabled) return;

            if (WhatIf) {
                // TODO WhatIf
                return;
            }
            else if (_options.GatewayId != default)
            {
                if (!Guid.TryParse(_options.GatewayId.ExpandParamsAndEnv(parameters), out var gatewayId))
                    throw new DeploymentException($"The GatewayId expression could not be resolved as a valid Guid: {_options.GatewayId}.");

                Log.Debug("Binding dataset to gateway: {GatewayId}", gatewayId);

                await _powerBI.Datasets.BindToGatewayInGroupAsync(workspaceId, datasetId,
                    new BindToGatewayRequest(
                        gatewayId,
                        ResolveDatasetSources(gatewayId, _options.DataSources, _powerBI)
                ));

                Log.Information("Successfully bound dataset to gateway: {GatewayId}", gatewayId);
            }
        }

        private static IList<Guid?> ResolveDatasetSources(Guid gatewayId, string[] datasources, IPowerBIClient powerbi)
        {
            if (datasources == null || datasources.Length == 0) return default;

            var gatewaySources = new Lazy<IList<GatewayDatasource>>(() => 
            {
                Log.Debug("Fetching datasources for gateway: {GatewayID}", gatewayId);
                var result = powerbi.Gateways.GetDatasources(gatewayId);
                return result.Value;
            });

            var sources = datasources.Aggregate(
                new List<Guid?>(),
                (list, value) => {
                    if (Guid.TryParse(value, out var id))
                    {
                        list.Add(id);
                    }
                    else
                    {
                        var match = gatewaySources.Value.FirstOrDefault(x =>
                            x.DatasourceName.Equals(value, StringComparison.InvariantCultureIgnoreCase)
                        );

                        if (match == null) {
                            throw new DeploymentException($"Failed to lookup datasource ID for {value} on gateway: {gatewayId}.");
                        }

                        Log.Debug("Resolved datasource '{DatasourceName}' as {DatasourceID}.", value, match.Id);
                        list.Add(match.Id);
                    }
                    return list;
                }
            );

            return sources;
        }

    }

}