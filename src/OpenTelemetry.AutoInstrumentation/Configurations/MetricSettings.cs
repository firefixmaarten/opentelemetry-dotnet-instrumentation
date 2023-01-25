// <copyright file="MetricSettings.cs" company="OpenTelemetry Authors">
// Copyright The OpenTelemetry Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>

namespace OpenTelemetry.AutoInstrumentation.Configurations;

/// <summary>
/// Metric Settings
/// </summary>
internal class MetricSettings : Settings
{
    /// <summary>
    /// Gets a value indicating whether the metrics should be loaded by the profiler. Default is true.
    /// </summary>
    public bool MetricsEnabled { get; private set; }

    /// <summary>
    /// Gets the metrics exporter.
    /// </summary>
    public MetricsExporter MetricExporter { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the console exporter is enabled.
    /// </summary>
    public bool ConsoleExporterEnabled { get; private set; }

    /// <summary>
    /// Gets the list of enabled meters.
    /// </summary>
    public IList<MetricInstrumentation> EnabledInstrumentations { get; private set; } = new List<MetricInstrumentation>();

    /// <summary>
    /// Gets the list of meters to be added to the MeterProvider at the startup.
    /// </summary>
    public IList<string> Meters { get; } = new List<string>();

    protected override void OnLoad(Configuration configuration)
    {
        MetricExporter = ParseMetricExporter(configuration);
        ConsoleExporterEnabled = configuration.GetBool(ConfigurationKeys.Metrics.ConsoleExporterEnabled) ?? false;

        EnabledInstrumentations = configuration.ParseEnabledEnumList<MetricInstrumentation>(
            enabledConfiguration: ConfigurationKeys.Metrics.Instrumentations,
            disabledConfiguration: ConfigurationKeys.Metrics.DisabledInstrumentations,
            error: "The \"{0}\" is not recognized as supported metrics instrumentation and cannot be enabled or disabled.");

        var additionalSources = configuration.GetString(ConfigurationKeys.Metrics.AdditionalSources);
        if (additionalSources != null)
        {
            foreach (var sourceName in additionalSources.Split(Constants.ConfigurationValues.Separator))
            {
                Meters.Add(sourceName);
            }
        }

        MetricsEnabled = configuration.GetBool(ConfigurationKeys.Metrics.MetricsEnabled) ?? true;
    }

    private static MetricsExporter ParseMetricExporter(Configuration configuration)
    {
        var metricsExporterEnvVar = configuration.GetString(ConfigurationKeys.Metrics.Exporter)
                                    ?? Constants.ConfigurationValues.Exporters.Otlp;

        switch (metricsExporterEnvVar)
        {
            case null:
            case "":
            case Constants.ConfigurationValues.Exporters.Otlp:
                return MetricsExporter.Otlp;
            case Constants.ConfigurationValues.Exporters.Prometheus:
                return MetricsExporter.Prometheus;
            case Constants.ConfigurationValues.None:
                return MetricsExporter.None;
            default:
                throw new FormatException($"Metric exporter '{metricsExporterEnvVar}' is not supported");
        }
    }
}