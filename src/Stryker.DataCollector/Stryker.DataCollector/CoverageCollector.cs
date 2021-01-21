using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollector.InProcDataCollector;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.InProcDataCollector;

namespace Stryker.DataCollector
{
    [DataCollectorFriendlyName("StrykerCoverage")]
    [DataCollectorTypeUri("https://stryker-mutator.io/")]
    public class CoverageCollector: InProcDataCollection
    {
        private IDataCollectionSink _dataSink;
        private bool _coverageOn;
        private int _activeMutation = -1;
        private Action<string> _logger;
        private readonly IDictionary<string, int> _mutantTestedBy = new Dictionary<string, int>();
        private int? _singleMutant;

        private string _controlClassName;
        private Type _controller;
        private FieldInfo _activeMutantField;
        private FieldInfo _logControlField;
        private FieldInfo _activeMutantSeenField;
        private FieldInfo _coverageControlField;

        private MethodInfo _getCoverageData;
        private IEnumerable<int> _mutantsToLog;
        private bool _mustLog;

        private const string TemplateForConfiguration = 
            @"<InProcDataCollectionRunSettings><InProcDataCollectors><InProcDataCollector {0}>
<Configuration>{1}</Configuration></InProcDataCollector></InProcDataCollectors></InProcDataCollectionRunSettings>";

        public const string StrykerCoverageId = "Stryker.Coverage";
        public const string StrykerMutantCoveredId = "Stryker.ActiveMutantCovered";

        public string MutantList => _singleMutant?.ToString() ?? string.Join(",", _mutantTestedBy.Values.Distinct());

        public static string GetVsTestSettings(bool needCoverage, Dictionary<int, IList<string>> mutantTestsMap, string helpNameSpace, IEnumerable<int> mutantsToLog)
        {
            var codeBase = typeof(CoverageCollector).GetTypeInfo().Assembly.Location;
            var qualifiedName = typeof(CoverageCollector).AssemblyQualifiedName;
            var friendlyName = typeof(CoverageCollector).ExtractAttribute<DataCollectorFriendlyNameAttribute>().FriendlyName;
            // ReSharper disable once PossibleNullReferenceException
            var uri = (typeof(CoverageCollector).GetTypeInfo().GetCustomAttributes(typeof(DataCollectorTypeUriAttribute), false).First() as
                DataCollectorTypeUriAttribute).TypeUri;
            var line= $"friendlyName=\"{friendlyName}\" uri=\"{uri}\" codebase=\"{codeBase}\" assemblyQualifiedName=\"{qualifiedName}\"";
            var configuration = new StringBuilder();
            configuration.Append("<Parameters>");

            if (needCoverage)
            {
                configuration.Append("<Coverage/>");
            }
            if (mutantTestsMap != null)
            {
                foreach ( var entry in mutantTestsMap)
                {
                    configuration.AppendFormat("<Mutant id='{0}' tests='{1}'/>", entry.Key,  entry.Value == null ? "" : string.Join(",", entry.Value));
                }
            }

            configuration.Append($"<MutantControl  name='{helpNameSpace}.MutantControl'/>");
            if (mutantTestsMap != null && mutantsToLog != null)
            {
                var mutantsOfInterest = string.Join(",", mutantsToLog.Where(mutantTestsMap.ContainsKey));
                if (!string.IsNullOrEmpty(mutantsOfInterest))
                {
                    configuration.Append($"<MutantsToLog>{mutantsOfInterest}</MutantsToLog>");
                }
            }
            configuration.Append("</Parameters>");
            
            return string.Format(TemplateForConfiguration, line, configuration);
        }

        public void Initialize(IDataCollectionSink dataCollectionSink)
        {
            this._dataSink = dataCollectionSink;
        }

        public void SetLogger(Action<string> logger)
        {
            _logger = logger;
        }

        private void Log(string message)
        {
            if (_logger != null)
            {
                _logger.Invoke(message);
            }
            else if (_mustLog)
            {
                Console.Error.WriteLine(message);
            }
        }

        // called before any test is run
        public void TestSessionStart(TestSessionStartArgs testSessionStartArgs)
        {            
            var configuration = testSessionStartArgs.Configuration;
            ReadConfiguration(configuration);
            // scan loaded assembly, just in case the test assembly is already loaded
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic);
            foreach (var assembly in assemblies)
            {
                FindControlType(assembly);
            }
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;

            Log($"Test Session start with conf {configuration}.");
        }

        private void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            var assembly = args.LoadedAssembly;
            FindControlType(assembly);
        }

        private void FindControlType(Assembly assembly)
        {
            if (_controller != null)
            {
                return;
            }

            _controller = assembly.ExportedTypes.FirstOrDefault(t => t.FullName == _controlClassName);
            if (_controller == null)
            {
                return;
            }

            _activeMutantField = _controller.GetField("ActiveMutant");
            _logControlField = _controller.GetField("MustLog");
            _coverageControlField = _controller.GetField("CaptureCoverage");
            _activeMutantSeenField = _controller.GetField("ActiveMutantSeen");
            _getCoverageData = _controller.GetMethod("GetCoverageData");

            if (_coverageOn)
            {
                _coverageControlField.SetValue(null, true);
            }
            SetActiveMutation(_activeMutation);
        }

        private void SetActiveMutation(int id)
        {
            if (this._mutantsToLog != null)
            {
                _mustLog = this._mutantsToLog.Contains(id);
                if (_logControlField != null)
                {
                    _logControlField.SetValue(null, _mustLog);
                }
            }
            _activeMutation = id;
            _activeMutantField?.SetValue(null, _activeMutation);
            _activeMutantSeenField?.SetValue(null, false);
        }

        private void ReadConfiguration(string configuration)
        {
            var node = new XmlDocument();
            node.LoadXml(configuration);

            var testMapping = node.SelectNodes("//Parameters/Mutant");
            if (testMapping !=null)
            {
                for (var i = 0; i < testMapping.Count; i++)
                {
                    var current = testMapping[i];
                    var id = int.Parse(current.Attributes["id"].Value);
                    var tests = current.Attributes["tests"].Value;
                    if (string.IsNullOrEmpty(tests))
                    {
                        _singleMutant = id;
                    }
                    else
                    {
                        foreach (var test in tests.Split(','))
                        {
                            _mutantTestedBy[test] = id;
                        }
                    }
                }
            }

            var nameSpaceNode = node.SelectSingleNode("//Parameters/MutantControl");
            if (nameSpaceNode != null)
            {
                _controlClassName = nameSpaceNode.Attributes["name"].Value;
            }

            var coverage = node.SelectSingleNode("//Parameters/Coverage");
            if (coverage != null)
            {
                _coverageOn = true;
            }

            var mutantsToLogConfiguration = node.SelectSingleNode("//Parameters/MutantsToLog");
            if (mutantsToLogConfiguration?.FirstChild?.Value != null)
            {
                _mutantsToLog = mutantsToLogConfiguration.FirstChild.Value.Split(',').Select(int.Parse);
            }
        }

        public void TestCaseStart(TestCaseStartArgs testCaseStartArgs)
        {
            if (_coverageOn)
            {
                return;
            }

            // we need to set the proper mutant
            var mutantId = _singleMutant ?? _mutantTestedBy[testCaseStartArgs.TestCase.Id.ToString()];
            SetActiveMutation(mutantId);

            Log($"Test {testCaseStartArgs.TestCase.FullyQualifiedName} starts against mutant {mutantId} (var).");
        }

        public void TestCaseEnd(TestCaseEndArgs testCaseEndArgs)
        {
            Log($"Test {testCaseEndArgs.DataCollectionContext.TestCase.FullyQualifiedName} ends.");

            if (!_coverageOn)
            {
                _dataSink.SendData(testCaseEndArgs.DataCollectionContext, StrykerMutantCoveredId, _activeMutantSeenField.GetValue(null).ToString());
                _activeMutantSeenField?.SetValue(null, false);
                return;
            }

            PublishCoverageData(testCaseEndArgs);
        }

        private void PublishCoverageData(TestCaseEndArgs testCaseEndArgs)
        {
            var coverData = RetrieveCoverData();
            // null means we failed to retrieve data
            if (coverData != null)
            {
                if (coverData.Length == 0)
                {
                    // we cannot report an empty string.
                    coverData = " ";
                }
                _dataSink.SendData(testCaseEndArgs.DataCollectionContext, StrykerCoverageId, coverData);
            }
            else
            {
                Log($"Failed to retrieve coverage data for {testCaseEndArgs.DataCollectionContext.TestCase.FullyQualifiedName}");
            }
        }

        private string RetrieveCoverData()
        {
            var covered = (IList<int>[]) _getCoverageData.Invoke(null, Array.Empty<object>());
            var coverData = string.Join(",", covered[0]) + ";" + string.Join(",", covered[1]);
            return coverData;
        }

        public void TestSessionEnd(TestSessionEndArgs testSessionEndArgs)
        {
            Log($"TestSession ends.");
        }
    }
}
