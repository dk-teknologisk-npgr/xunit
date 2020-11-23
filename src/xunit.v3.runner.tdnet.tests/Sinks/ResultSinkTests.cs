﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using TestDriven.Framework;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runner.TdNet;
using Xunit.v3;

public class ResultSinkTests
{
	[Fact]
	public static async void SignalsFinishedEventUponReceiptOfITestAssemblyFinished()
	{
		var listener = Substitute.For<ITestListener>();
		await using var sink = new ResultSink(listener, 42);
		var message = TestData.TestAssemblyFinished();

		sink.OnMessage(message);

		Assert.True(sink.Finished.WaitOne(0));
	}

	public class RunState
	{
		[Fact]
		public static async void DefaultRunStateIsNoTests()
		{
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42);

			Assert.Equal(TestRunState.NoTests, sink.TestRunState);
		}

		[Theory]
		[InlineData(TestRunState.NoTests)]
		[InlineData(TestRunState.Error)]
		[InlineData(TestRunState.Success)]
		public static async void FailureSetsStateToFailed(TestRunState initialState)
		{
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = initialState };
			sink.OnMessage(TestData.TestClassStarting());
			sink.OnMessage(TestData.TestMethodStarting());
			sink.OnMessage(TestData.TestStarting());

			sink.OnMessage(TestData.TestFailed());

			Assert.Equal(TestRunState.Failure, sink.TestRunState);
		}

		[Fact]
		public static async void Success_MovesToSuccess()
		{
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = TestRunState.NoTests };
			sink.OnMessage(TestData.TestClassStarting());
			sink.OnMessage(TestData.TestMethodStarting());
			sink.OnMessage(TestData.TestStarting());

			sink.OnMessage(TestData.TestPassed());

			Assert.Equal(TestRunState.Success, sink.TestRunState);
		}

		[Theory]
		[InlineData(TestRunState.Error)]
		[InlineData(TestRunState.Failure)]
		[InlineData(TestRunState.Success)]
		public static async void Success_StaysInCurrentState(TestRunState initialState)
		{
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = initialState };
			sink.OnMessage(TestData.TestClassStarting());
			sink.OnMessage(TestData.TestMethodStarting());
			sink.OnMessage(TestData.TestStarting());

			sink.OnMessage(TestData.TestPassed());

			Assert.Equal(initialState, sink.TestRunState);
		}

		[Fact]
		public static async void Skip_MovesToSuccess()
		{
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = TestRunState.NoTests };
			sink.OnMessage(TestData.TestClassStarting());
			sink.OnMessage(TestData.TestMethodStarting());
			sink.OnMessage(TestData.TestStarting());

			sink.OnMessage(TestData.TestSkipped());

			Assert.Equal(TestRunState.Success, sink.TestRunState);
		}

		[Theory]
		[InlineData(TestRunState.Error)]
		[InlineData(TestRunState.Failure)]
		[InlineData(TestRunState.Success)]
		public static async void Skip_StaysInCurrentState(TestRunState initialState)
		{
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = initialState };
			sink.OnMessage(TestData.TestClassStarting());
			sink.OnMessage(TestData.TestMethodStarting());
			sink.OnMessage(TestData.TestStarting());

			sink.OnMessage(TestData.TestSkipped());

			Assert.Equal(initialState, sink.TestRunState);
		}
	}

	public class FailureInformation
	{
		readonly string assemblyID = "assembly-id";
		readonly string classID = "test-class-id";
		readonly string collectionID = "test-collection-id";
		readonly int[] exceptionParentIndices = new[] { -1 };
		readonly string[] exceptionTypes = new[] { "ExceptionType" };
		readonly string[] messages = new[] { "This is my message \t\r\n" };
		readonly string methodID = "test-method-id";
		readonly string[] stackTraces = new[] { "Line 1\r\nLine 2\r\nLine 3" };
		readonly string testCaseID = "test-case-id";
		//readonly string testID = "test-id";

		static TMessageType MakeFailureInformationSubstitute<TMessageType>()
			where TMessageType : class, IFailureInformation
		{
			var result = Substitute.For<TMessageType>();
			result.ExceptionTypes.Returns(new[] { "ExceptionType" });
			result.Messages.Returns(new[] { "This is my message \t\r\n" });
			result.StackTraces.Returns(new[] { "Line 1\r\nLine 2\r\nLine 3" });
			return result;
		}

		public static IEnumerable<object[]> Messages
		{
			get
			{
				yield return new object[] { MakeFailureInformationSubstitute<IErrorMessage>(), "Fatal Error" };

				var testCase = Mocks.TestCase(typeof(object), "ToString", displayName: "MyTestCase");
				var testCleanupFailure = MakeFailureInformationSubstitute<ITestCleanupFailure>();
				var test = Mocks.Test(testCase, "MyTest");
				testCleanupFailure.Test.Returns(test);
				yield return new object[] { testCleanupFailure, "Test Cleanup Failure (MyTest)" };
			}
		}

		[Fact]
		public async ValueTask TestAssemblyCleanupFailure()
		{
			var collectionStarting = new _TestAssemblyStarting
			{
				AssemblyUniqueID = assemblyID,
				AssemblyPath = "assembly-file-path"
			};
			var collectionCleanupFailure = new _TestAssemblyCleanupFailure
			{
				AssemblyUniqueID = assemblyID,
				ExceptionParentIndices = exceptionParentIndices,
				ExceptionTypes = exceptionTypes,
				Messages = messages,
				StackTraces = stackTraces
			};
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = TestRunState.NoTests };

			sink.OnMessage(collectionStarting);
			sink.OnMessage(collectionCleanupFailure);

			AssertFailureInformation(listener, sink.TestRunState, "Test Assembly Cleanup Failure (assembly-file-path)");
		}

		[Fact]
		public async ValueTask TestCaseCleanupFailure()
		{
			var caseStarting = new _TestCaseStarting
			{
				AssemblyUniqueID = assemblyID,
				TestCaseUniqueID = testCaseID,
				TestCaseDisplayName = "MyTestCase",
				TestClassUniqueID = classID,
				TestCollectionUniqueID = collectionID,
				TestMethodUniqueID = methodID
			};
			var caseCleanupFailure = new _TestCaseCleanupFailure
			{
				AssemblyUniqueID = assemblyID,
				ExceptionParentIndices = exceptionParentIndices,
				ExceptionTypes = exceptionTypes,
				Messages = messages,
				StackTraces = stackTraces,
				TestCaseUniqueID = testCaseID,
				TestCollectionUniqueID = collectionID,
				TestClassUniqueID = classID,
				TestMethodUniqueID = methodID
			};
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = TestRunState.NoTests };

			sink.OnMessage(caseStarting);
			sink.OnMessage(caseCleanupFailure);

			AssertFailureInformation(listener, sink.TestRunState, "Test Case Cleanup Failure (MyTestCase)");
		}

		[Fact]
		public async ValueTask TestClassCleanupFailure()
		{
			var classStarting = new _TestClassStarting
			{
				AssemblyUniqueID = assemblyID,
				TestClass = "MyType",
				TestClassUniqueID = classID,
				TestCollectionUniqueID = collectionID
			};
			var classCleanupFailure = new _TestClassCleanupFailure
			{
				AssemblyUniqueID = assemblyID,
				ExceptionParentIndices = exceptionParentIndices,
				ExceptionTypes = exceptionTypes,
				Messages = messages,
				StackTraces = stackTraces,
				TestCollectionUniqueID = collectionID,
				TestClassUniqueID = classID
			};
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = TestRunState.NoTests };

			sink.OnMessage(classStarting);
			sink.OnMessage(classCleanupFailure);

			AssertFailureInformation(listener, sink.TestRunState, "Test Class Cleanup Failure (MyType)");
		}

		[Fact]
		public async ValueTask TestCollectionCleanupFailure()
		{
			var collectionStarting = new _TestCollectionStarting
			{
				AssemblyUniqueID = assemblyID,
				TestCollectionDisplayName = "FooBar",
				TestCollectionUniqueID = collectionID
			};
			var collectionCleanupFailure = new _TestCollectionCleanupFailure
			{
				AssemblyUniqueID = assemblyID,
				ExceptionParentIndices = exceptionParentIndices,
				ExceptionTypes = exceptionTypes,
				Messages = messages,
				StackTraces = stackTraces,
				TestCollectionUniqueID = collectionID
			};
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = TestRunState.NoTests };

			sink.OnMessage(collectionStarting);
			sink.OnMessage(collectionCleanupFailure);

			AssertFailureInformation(listener, sink.TestRunState, "Test Collection Cleanup Failure (FooBar)");
		}

		[Fact]
		public async ValueTask TestMethodCleanupFailure()
		{
			var methodStarting = new _TestMethodStarting
			{
				AssemblyUniqueID = assemblyID,
				TestClassUniqueID = classID,
				TestCollectionUniqueID = collectionID,
				TestMethod = "MyMethod",
				TestMethodUniqueID = methodID,
			};
			var methodCleanupFailure = new _TestMethodCleanupFailure
			{
				AssemblyUniqueID = assemblyID,
				ExceptionParentIndices = exceptionParentIndices,
				ExceptionTypes = exceptionTypes,
				Messages = messages,
				StackTraces = stackTraces,
				TestCollectionUniqueID = collectionID,
				TestClassUniqueID = classID,
				TestMethodUniqueID = methodID
			};
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = TestRunState.NoTests };

			sink.OnMessage(methodStarting);
			sink.OnMessage(methodCleanupFailure);

			AssertFailureInformation(listener, sink.TestRunState, "Test Method Cleanup Failure (MyMethod)");
		}

		[Theory]
		[MemberData(nameof(Messages), DisableDiscoveryEnumeration = true)]
		public static async void LogsTestFailure(IMessageSinkMessage message, string messageType)
		{
			var listener = Substitute.For<ITestListener>();
			await using var sink = new ResultSink(listener, 42) { TestRunState = TestRunState.NoTests };

			sink.OnMessage(message);

			AssertFailureInformation(listener, sink.TestRunState, messageType);
		}

		static void AssertFailureInformation(ITestListener listener, TestRunState testRunState, string messageType)
		{
			Assert.Equal(TestRunState.Failure, testRunState);
			var testResult = listener.Captured(x => x.TestFinished(null)).Arg<TestResult>();
			Assert.Equal($"*** {messageType} ***", testResult.Name);
			Assert.Equal(TestState.Failed, testResult.State);
			Assert.Equal(1, testResult.TotalTests);
			Assert.Equal("ExceptionType : This is my message \t\r\n", testResult.Message);
			Assert.Equal("Line 1\r\nLine 2\r\nLine 3", testResult.StackTrace);
		}
	}

	public class MessageConversion
	{
		[Fact]
		public static async void ConvertsTestPassed()
		{
			TestResult? testResult = null;
			var listener = Substitute.For<ITestListener>();
			listener
				.WhenAny(l => l.TestFinished(null))
				.Do<TestResult>(result => testResult = result);
			await using var sink = new ResultSink(listener, 42);
			sink.OnMessage(TestData.TestClassStarting(testClass: typeof(object).FullName!));
			sink.OnMessage(TestData.TestMethodStarting(testMethod: nameof(object.GetHashCode)));
			sink.OnMessage(TestData.TestStarting(testDisplayName: "Display Name"));

			sink.OnMessage(TestData.TestPassed(executionTime: 123.45m));

			Assert.NotNull(testResult);
			Assert.Same(typeof(object), testResult.FixtureType);
			Assert.Equal(nameof(object.GetHashCode), testResult.Method.Name);
			Assert.Equal("Display Name", testResult.Name);
			Assert.Equal(TestState.Passed, testResult.State);
			Assert.Equal(123.45, testResult.TimeSpan.TotalMilliseconds);
			Assert.Equal(42, testResult.TotalTests);
		}

		[Fact]
		public static async void ConvertsTestFailed()
		{
			_IErrorMetadata errorMetadata;

			try
			{
				throw new Exception();
			}
			catch (Exception e)
			{
				errorMetadata = ExceptionUtility.ConvertExceptionToErrorMetadata(e);
			}

			TestResult? testResult = null;
			var listener = Substitute.For<ITestListener>();
			listener
				.WhenAny(l => l.TestFinished(null))
				.Do<TestResult>(result => testResult = result);
			await using var sink = new ResultSink(listener, 42);
			sink.OnMessage(TestData.TestClassStarting(testClass: typeof(object).FullName!));
			sink.OnMessage(TestData.TestMethodStarting(testMethod: nameof(object.GetHashCode)));
			sink.OnMessage(TestData.TestStarting(testDisplayName: "Display Name"));
			var message = TestData.TestFailed(
				exceptionParentIndices: errorMetadata.ExceptionParentIndices,
				exceptionTypes: errorMetadata.ExceptionTypes,
				executionTime: 123.45m,
				messages: errorMetadata.Messages,
				stackTraces: errorMetadata.StackTraces
			);

			sink.OnMessage(message);

			Assert.NotNull(testResult);
			Assert.Same(typeof(object), testResult.FixtureType);
			Assert.Equal(nameof(object.GetHashCode), testResult.Method.Name);
			Assert.Equal("Display Name", testResult.Name);
			Assert.Equal(TestState.Failed, testResult.State);
			Assert.Equal(123.45, testResult.TimeSpan.TotalMilliseconds);
			Assert.Equal(42, testResult.TotalTests);
			Assert.Equal($"{errorMetadata.ExceptionTypes[0]} : {errorMetadata.Messages[0]}", testResult.Message);
			Assert.Equal(errorMetadata.StackTraces[0], testResult.StackTrace);
		}

		[Fact]
		public static async void ConvertsTestSkipped()
		{
			TestResult? testResult = null;
			var listener = Substitute.For<ITestListener>();
			listener
				.WhenAny(l => l.TestFinished(null))
				.Do<TestResult>(result => testResult = result);
			await using var sink = new ResultSink(listener, 42);
			sink.OnMessage(TestData.TestClassStarting(testClass: typeof(object).FullName!));
			sink.OnMessage(TestData.TestMethodStarting(testMethod: nameof(object.GetHashCode)));
			sink.OnMessage(TestData.TestStarting(testDisplayName: "Display Name"));

			sink.OnMessage(TestData.TestSkipped(reason: "I forgot how to run", executionTime: 123.45m));

			Assert.NotNull(testResult);
			Assert.Same(typeof(object), testResult.FixtureType);
			Assert.Equal(nameof(object.GetHashCode), testResult.Method.Name);
			Assert.Equal("Display Name", testResult.Name);
			Assert.Equal(TestState.Ignored, testResult.State);
			Assert.Equal(123.45, testResult.TimeSpan.TotalMilliseconds);
			Assert.Equal(42, testResult.TotalTests);
			Assert.Equal("I forgot how to run", testResult.Message);
		}
	}
}
