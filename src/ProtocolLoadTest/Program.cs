﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Nest;

namespace ProtocolLoadTest
{
	class Program
	{
		const string INDEX_PREFIX = "proto-load-test-";
		const int HTTP_PORT = 9200;
		const int THRIFT_PORT = 9500;

		// Total number of messages to send to ElasticSearch
		const int NUM_MESSAGES = 250000;

		// Number of messages to buffer before sending via bulk API
		const int BUFFER_SIZE = 1000;

		static void Main(string[] args)
		{
			double httpRate = RunTest<HttpTester>(HTTP_PORT);
			double manualAsyncHttpRate = RunTest<HttpManualAsyncTester>(HTTP_PORT);
			//double thriftRate = RunTest<ThriftTester>(THRIFT_PORT);

			Console.WriteLine();
			Console.WriteLine("HTTP (IndexManyAsync): {0:0,0}/s", httpRate);
			Console.WriteLine("HTTP (IndexMany + TaskFactory.StartNew): {0:0,0}/s", manualAsyncHttpRate);
			//Console.WriteLine("Thrift: {0:0,0}/s", thriftRate);

			Console.ReadLine();
		}

		private static double RunTest<T>(int port) where T : ITester
		{
			string type = typeof(T).Name.ToLower();
			Console.WriteLine("Starting {0} test", type);

			// Recreate index up-front, so this process doesn't interfere with perf figures
			RecreateIndex(type);

			Stopwatch sw = new Stopwatch();
			sw.Start();

			var tester = Activator.CreateInstance<T>();

			tester.Run(INDEX_PREFIX + type, port, NUM_MESSAGES, BUFFER_SIZE);

			sw.Stop();
			double rate = NUM_MESSAGES / ((double)sw.ElapsedMilliseconds / 1000);

			Console.WriteLine("{0} test completed in {1}ms ({2:0,0}/s)", type, sw.ElapsedMilliseconds, rate);

			// Close the index so we don't interfere with the next test
			CloseIndex(type);

			return rate;
		}

		private static void RecreateIndex(string suffix)
		{
			var host = "localhost";
			if (Process.GetProcessesByName("fiddler").Any())
				host = "ipv4.fiddler";
			string indexName = INDEX_PREFIX + suffix;

			var connSettings = new ConnectionSettings(new Uri("http://"+host+":9200"))
				.SetDefaultIndex(indexName);

			var client = new ElasticClient(connSettings);

			ConnectionStatus connStatus;

			if (!client.TryConnect(out connStatus))
			{
				Console.Error.WriteLine("Could not connect to {0}:\r\n{1}",
					connSettings.Host, connStatus.Error.OriginalException.Message);
				Console.Read();
				return;
			}

			client.DeleteIndex(indexName);

			var indexSettings = new IndexSettings();
			indexSettings.NumberOfReplicas = 1;
			indexSettings.NumberOfShards = 5;
			indexSettings.Add("index.refresh_interval", "-1");

			var createResponse = client.CreateIndex(indexName, indexSettings);
			client.MapFromAttributes<Message>();
		}

		private static void CloseIndex(string suffix)
		{
			string indexName = INDEX_PREFIX + suffix;

			var host = "localhost";
			if (Process.GetProcessesByName("fiddler").Any())
				host = "ipv4.fiddler";

			var connSettings = new ConnectionSettings(new Uri("http://" + host + ":9200"))
				.SetDefaultIndex(indexName);

			var client = new ElasticClient(connSettings);

			ConnectionStatus connStatus;

			if (!client.TryConnect(out connStatus))
			{
				Console.Error.WriteLine("Could not connect to {0}:\r\n{1}",
					connSettings.Host, connStatus.Error.OriginalException.Message);
				Console.Read();
				return;
			}

			client.CloseIndex(indexName);
		}
	}
}
