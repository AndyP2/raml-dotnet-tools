﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NUnit.Framework;
using Raml.Parser.Expressions;
using Raml.Tools.ClientGenerator;

namespace Raml.Tools.Tests
{
	[TestFixture]
	public class MultipleHeadersTest
	{


		[Test]
		public void Should_Build_Multiple_Headers_When_Many_Response_With_Headers()
		{
			var doc = new RamlDocument {Title = "test"};

			var okHeaders = new List<Parameter>
			                {
				                new Parameter
				                {
					                DisplayName = "code",
					                Type = "integer"
				                },
				                new Parameter
				                {
					                DisplayName = "token",
					                Type = "string"
				                }
			                };

			var errorHeaders = new List<Parameter>
			                {
				                new Parameter
				                {
					                DisplayName = "code",
					                Type = "integer"
				                },
				                new Parameter
				                {
					                DisplayName = "error",
					                Type = "string"
				                }
			                };

			var badRequestHeaders = new List<Parameter>
			                {
				                new Parameter
				                {
					                DisplayName = "code",
					                Type = "integer"
				                },
				                new Parameter
				                {
					                DisplayName = "description",
					                Type = "string"
				                }
			                };

			var okResponse = new Response
			                 {
				                 Code = "200",
								 Headers = okHeaders
			                 };

			var errorResponse = new Response
			                    {
				                    Code = "400",
									Headers = errorHeaders
			                    };

			var notFoundResponse = new Response
			                       {
				                       Code = "404",
				                       Headers = badRequestHeaders
			                       };

			var methods = new List<Method>
			              {
				              new Method
				              {
					              Verb = "get",
					              Responses = new[] { okResponse, errorResponse, notFoundResponse }
				              }
			              };

			var resources = new Collection<Resource>
			                {
				                new Resource
				                {
					                RelativeUri = "movies",
					                Methods = methods
				                }
			                };

			doc.Resources = resources;

			var service = new ClientGeneratorService(doc, "test");
			var model = service.BuildModel();
			Assert.AreEqual(4, model.ResponseHeaderObjects.Count);

			var multipleModel = model.ResponseHeaderObjects.First(o => o.Key.Contains("Multiple")).Value;

			Assert.AreEqual(3, multipleModel.Properties.Count);

			Assert.AreEqual("Models." + model.ResponseHeaderObjects.First(o => o.Key.Contains("Multiple")).Value.Name,
				model.Classes.First().Methods.First().ResponseHeaderType);
		}


	}
}