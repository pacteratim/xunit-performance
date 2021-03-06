﻿using Microsoft.Xunit.Performance.Api.Table;
using Microsoft.Xunit.Performance.Sdk;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

namespace Microsoft.Xunit.Performance.Api
{
    [Serializable]
    [XmlRoot("assemblies")]
    public sealed class AssemblyModelCollection : List<AssemblyModel>
    {
        public void Serialize(string xmlFileName)
        {
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add("", "");
            using (var stream = File.Create(xmlFileName))
            {
                using (var sw = new StreamWriter(stream))
                {
                    new XmlSerializer(typeof(AssemblyModelCollection))
                        .Serialize(sw, this, namespaces);
                }
            }
        }
    }

    [Serializable]
    [XmlType("assembly")]
    public sealed class AssemblyModel
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlArray("collection")]
        public List<TestModel> Collection { get; set; }

        internal DataTable GetStatistics()
        {
            var dt = new DataTable();
            var col0_testName = dt.AddColumn(TableHeader.TestName);
            var col1_metric = dt.AddColumn(TableHeader.Metric);
            var col2_iterations = dt.AddColumn(TableHeader.Iterations);
            var col3_average = dt.AddColumn(TableHeader.AVG);
            var col4_stdevs = dt.AddColumn(TableHeader.SD);
            var col5_min = dt.AddColumn(TableHeader.MIN);
            var col6_max = dt.AddColumn(TableHeader.MAX);

            foreach (var testModel in Collection)
            {
                foreach (var metric in testModel.Performance.Metrics)
                {
                    var values = testModel.Performance.IterationModels
                        .Where(iter => iter.Iteration.ContainsKey(metric.Name))
                        .Select(iter => iter.Iteration[metric.Name]);

                    if (values.Count() == 0) // Cannot compute statistics when there are not results (e.g. user only ran a subset of all tests).
                        continue;

                    // Skip the warmup run.
                    if (values.Count() > 1)
                        values = values.Skip(1);

                    var avg = values.Average();
                    var stdev_s = Math.Sqrt(values.Sum(x => Math.Pow(x - avg, 2)) / (values.Count() - 1));
                    var max = values.Max();
                    var min = values.Min();

                    var newRow = dt.AppendRow();
                    newRow[col0_testName] = testModel.Name;
                    newRow[col1_metric] = metric.DisplayName;

                    newRow[col2_iterations] = values.Count().ToString();
                    newRow[col3_average] = avg.ToString();
                    newRow[col4_stdevs] = stdev_s.ToString();
                    newRow[col5_min] = min.ToString();
                    newRow[col6_max] = max.ToString();
                }
            }

            return dt;
        }

        internal static AssemblyModel Create(
            string assemblyFileName,
            CSVMetricReader reader,
            XUnitPerformanceMetricData xUnitPerformanceMetricData)
        {
            var assemblyModel = new AssemblyModel {
                Name = Path.GetFileName(assemblyFileName),
                Collection = new List<TestModel>()
            };

            foreach (var (perfTestMsg, metric, values) in GetCollectedData(assemblyFileName, reader, xUnitPerformanceMetricData))
            {
                var testModel = assemblyModel.Collection.FirstOrDefault(test => test.Name == perfTestMsg.TestCase.DisplayName);
                if (testModel == null)
                {
                    testModel = new TestModel {
                        Name = perfTestMsg.TestCase.DisplayName,
                        Method = perfTestMsg.TestCase.TestMethod.Method.Name,
                        ClassName = perfTestMsg.TestCase.TestMethod.TestClass.Class.Name,
                        Performance = new PerformanceModel {
                            Metrics = new List<MetricModel>(),
                            IterationModels = new List<IterationModel>()
                        },
                    };
                }

                var testMetric = testModel.Performance.Metrics.FirstOrDefault(m => m.DisplayName == metric);
                if (testMetric == null)
                {
                    testModel.Performance.Metrics.Add(new MetricModel {
                        DisplayName = metric,
                        Name = metric,
                        Unit = metric == "Duration" ? PerformanceMetricUnits.Milliseconds : "unknown", // We are guessing here.
                    });
                }

                foreach (var value in values)
                {
                    var iterationModel = new IterationModel { Iteration = new Dictionary<string, double>() };
                    iterationModel.Iteration.Add(metric, value);
                    if (iterationModel.Iteration.Count > 0)
                        testModel.Performance.IterationModels.Add(iterationModel);
                }

                assemblyModel.Collection.Add(testModel);
            }

            return assemblyModel;
        }

        private static IEnumerable<(PerformanceTestMessage performanceTestMessage, string metric, IEnumerable<double> values)> GetCollectedData(
            string assemblyFileName,
            CSVMetricReader reader,
            XUnitPerformanceMetricData xUnitPerformanceMetricData)
        {
            var testsFoundInAssembly = xUnitPerformanceMetricData.PerformanceTestMessages;
            foreach (var testFoundInAssembly in testsFoundInAssembly)
            {
                foreach (var (testCaseName, metric, values) in GetMeasurements(reader))
                {
                    if (values == null)
                        continue;

                    if (testCaseName == testFoundInAssembly.TestCase.DisplayName)
                    {
                        yield return (testFoundInAssembly, metric, values);
                    }
                }
            }
        }

        private static IEnumerable<(string testCaseName, string metric, IEnumerable<double> values)> GetMeasurements(CSVMetricReader reader)
        {
            foreach (var testCaseName in reader.TestCases)
            {
                var iterations = reader.GetValues(testCaseName);
                var measurements = new Dictionary<string, List<double>>();

                foreach (var dict in iterations)
                {
                    foreach (var pair in dict)
                    {
                        if (!measurements.ContainsKey(pair.Key))
                            measurements[pair.Key] = new List<double>();
                        measurements[pair.Key].Add(pair.Value);
                    }
                }

                foreach (var measurement in measurements)
                    yield return (testCaseName, measurement.Key, measurement.Value);
            }
        }
    }

    [Serializable]
    [XmlRoot("ScenarioBenchmark")]
    public sealed class ScenarioBenchmark
    {
        [XmlAttribute("Name")]
        public string Name { get; set; }

        [XmlAttribute("Namespace")]
        public string Namespace { get; set; }

        [XmlArray("Tests")]
        public List<ScenarioTestModel> Tests { get; set; }

        public ScenarioBenchmark()
        {
            Namespace = "";
            Tests = new List<ScenarioTestModel>();
        }

        public ScenarioBenchmark(string name) : this()
        {
            Name = name;
        }

        public void Serialize(string xmlFileName)
        {
            var namespaces = new XmlSerializerNamespaces();
            namespaces.Add("", "");
            using (var stream = File.Create(xmlFileName))
            {
                using (var sw = new StreamWriter(stream))
                {
                    new XmlSerializer(typeof(ScenarioBenchmark))
                        .Serialize(sw, this, namespaces);
                }
            }
        }

        internal DataTable GetStatistics()
        {
            var dt = new DataTable();
            var col0_testName = dt.AddColumn(TableHeader.TestName);
            var col1_metric = dt.AddColumn(TableHeader.Metric);
            var col2_iterations = dt.AddColumn(TableHeader.Iterations);
            var col3_average = dt.AddColumn(TableHeader.AVG);
            var col4_stdevs = dt.AddColumn(TableHeader.SD);
            var col5_min = dt.AddColumn(TableHeader.MIN);
            var col6_max = dt.AddColumn(TableHeader.MAX);

            foreach (var test in Tests)
            {
                foreach (var metric in test.Performance.Metrics)
                {
                    var values = test.Performance.IterationModels
                        .Where(iter => iter.Iteration.ContainsKey(metric.Name))
                        .Select(iter => iter.Iteration[metric.Name]);

                    if (values.Count() == 0) // Cannot compute statistics when there are not results (e.g. user only ran a subset of all tests).
                        continue;

                    // Skip the warmup run.
                    if (values.Count() > 1)
                        values = values.Skip(1);

                    var avg = values.Average();
                    var stdev_s = Math.Sqrt(values.Sum(x => Math.Pow(x - avg, 2)) / (values.Count() - 1));
                    var max = values.Max();
                    var min = values.Min();

                    var newRow = dt.AppendRow();
                    newRow[col0_testName] = test.Name;
                    newRow[col1_metric] = metric.DisplayName;

                    newRow[col2_iterations] = values.Count().ToString();
                    newRow[col3_average] = avg.ToString();
                    newRow[col4_stdevs] = stdev_s.ToString();
                    newRow[col5_min] = min.ToString();
                    newRow[col6_max] = max.ToString();
                }
            }

            return dt;
        }
    }

    [Serializable]
    [XmlType("Test")]
    public sealed class ScenarioTestModel
    {
        [XmlAttribute("Name")]
        public string Name { get; set; }

        [XmlAttribute("Namespace")]
        public string Namespace { get; set; }

        [XmlElement("Performance")]
        public PerformanceModel Performance { get; set; }

        public ScenarioTestModel()
        {
            Namespace = "";
            Performance = new PerformanceModel {
                Metrics = new List<MetricModel>(),
                IterationModels = new List<IterationModel>()
            };
        }

        public ScenarioTestModel(string name) : this()
        {
            Name = name;
        }
    }

    [Serializable]
    [XmlType("test")]
    public sealed class TestModel
    {
        [XmlAttribute("name")]
        public string Name { get; set; }

        [XmlAttribute("type")]
        public string ClassName { get; set; }

        [XmlAttribute("method")]
        public string Method { get; set; }

        [XmlElement("performance")]
        public PerformanceModel Performance { get; set; }
    }

    public sealed class PerformanceModel : IXmlSerializable
    {
        public List<MetricModel> Metrics { get; set; }

        public List<IterationModel> IterationModels { get; set; }

        public XmlSchema GetSchema()
        {
            return null;
        }

        public void ReadXml(XmlReader reader)
        {
            throw new NotImplementedException();
        }

        public void WriteXml(XmlWriter writer)
        {
            writer.WriteStartElement("metrics");
            foreach (var metric in Metrics)
            {
                writer.WriteStartElement(metric.Name);
                writer.WriteAttributeString("displayName", metric.DisplayName);
                writer.WriteAttributeString("unit", metric.Unit);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();

            writer.WriteStartElement("iterations");
            var index = 0;
            foreach (var iterationModel in IterationModels)
            {
                writer.WriteStartElement("iteration");
                writer.WriteAttributeString("index", index.ToString());
                ++index;
                foreach (var kvp in iterationModel.Iteration)
                {
                    writer.WriteAttributeString(kvp.Key, kvp.Value.ToString(CultureInfo.InvariantCulture));
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }
    }

    public sealed class MetricModel
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string Unit { get; set; }
    }

    public sealed class IterationModel
    {
        public Dictionary<string, double> Iteration { get; set; }
    }
}
