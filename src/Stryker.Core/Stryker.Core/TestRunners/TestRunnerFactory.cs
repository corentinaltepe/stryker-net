using Microsoft.Extensions.Logging;
using Stryker.Core.Initialisation;
using Stryker.Core.Logging;
using Stryker.Core.Options;
using Stryker.Core.Testing;
using Stryker.Core.TestRunners.VsTest;
using System.Linq;

namespace Stryker.Core.TestRunners
{
    public class TestRunnerFactory
    {
        private readonly ILogger _logger;

        public TestRunnerFactory()
        {
            _logger = ApplicationLogging.LoggerFactory.CreateLogger<TestRunnerFactory>();
        }

        public ITestRunner Create(StrykerOptions options, OptimizationFlags flags, ProjectInfo projectInfo)
        {
            _logger.LogInformation("Initializing test runners ({0})", options.TestRunner);
            ITestRunner testRunner;

            switch (options.TestRunner)
            {
                default:
                    _logger.LogWarning($"Testrunner {options.TestRunner} is not supported, switching to {TestRunner.VsTest}.");
                    goto case TestRunner.VsTest;
                case TestRunner.VsTest:
                    testRunner = new VsTestRunnerPool(options, flags, projectInfo);
                    break;
            }
            _logger.LogInformation("Test runners are ready");
            return testRunner;
        }
    }
}
