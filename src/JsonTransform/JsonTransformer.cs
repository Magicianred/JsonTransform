﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

using JsonTransform.Transformations;
using Newtonsoft.Json.Linq;

namespace JsonTransform
{
	/// <summary>
	/// Static JSON transformer.
	/// </summary>
	public static class JsonTransformer
	{
		/// <summary>
		/// Merge settings.
		/// </summary>
		private static readonly JsonMergeSettings MergeSettings = new JsonMergeSettings
		{
			MergeArrayHandling = MergeArrayHandling.Merge,
			MergeNullValueHandling = MergeNullValueHandling.Ignore,
		};

		/// <summary>
		/// Dictionary of transformation activators.
		/// </summary>
		private static readonly ConcurrentDictionary<string, TransformationActivator> Activators;

		/// <summary>
		/// Static constructor.
		/// </summary>
		static JsonTransformer()
		{
			Activators = new ConcurrentDictionary<string, TransformationActivator>();

			RegisterTransformationInternal("copy", ctx => new CopyTransformation(ctx), Constants.TransformPrefix);
			RegisterTransformationInternal("foreach", ctx => new ForEachTransformation(ctx), Constants.TransformPrefix);
			RegisterTransformationInternal("remove", ctx => new RemoveTransformation(ctx), Constants.TransformPrefix);
			RegisterTransformationInternal("setnull", ctx => new SetNullTransformation(ctx), Constants.TransformPrefix);
			RegisterTransformationInternal("union", ctx => new UnionTransformation(ctx), Constants.TransformPrefix);
		}

		/// <summary>
		/// Transform specified JSON-object with specified transformation.
		/// </summary>
		/// <param name="source">Source JSON-object.</param>
		/// <param name="transformation">Transformation meta object.</param>
		/// <returns>Transformed JSON-object.</returns>
		public static ITransformationResult Transform(JObject source, JObject transformation)
		{
			var errors = new List<string>();
			var resultObject = Transform(source, transformation, null, (sender, args) => errors.Add($"{args.TargetPath}: {args.Message}"));

			return new TransformationResult(resultObject, errors);
		}

		/// <summary>
		/// Transform specified string with JSON with specified transformation.
		/// </summary>
		/// <param name="source">Source JSON string.</param>
		/// <param name="transformDescription">String with transformation meta.</param>
		/// <returns>Transformed JSON-string.</returns>
		public static ITransformationResult Transform(string source, string transformDescription)
		{
			var sourceObject = JObject.Parse(source);
			var transformationObject = JObject.Parse(transformDescription);

			return Transform(sourceObject, transformationObject);
		}

		/// <summary>
		/// Register custom transformation.
		/// </summary>
		/// <param name="code">Code of custom transformation.</param>
		/// <param name="activator">Activator of transformation.</param>
		public static void RegisterTransformation(string code, TransformationActivator activator)
		{
			foreach (var symbol in code)
			{
				if (symbol <= 'a' || symbol >= 'z')
				{
					throw new ArgumentException($"Register code {code} failure. Code must contain only lower-case letters.");
				}
			}

			RegisterTransformationInternal(code, activator, Constants.CustomTransformPrefix);
		}

		private static void RegisterTransformationInternal(string code, TransformationActivator activator, string prefix)
		{
			var formattedCode = prefix + code;

			Activators[formattedCode] = activator;
		}

		/// <summary>
		/// Transform specified JSON-object with specified transformation.
		/// </summary>
		/// <param name="sourceObject">Source JSON-object.</param>
		/// <param name="transformationObject">Transformation meta object.</param>
		/// <param name="transformContext">Transformation context.</param>
		/// <param name="errorHandler">Error handler.</param>
		/// <returns>Transformation result object.</returns>
		internal static JObject Transform(
			JObject sourceObject,
			JObject transformationObject,
			ITransformationInvokeContext transformContext,
			EventHandler<TransformErrorEventArgs> errorHandler)
		{
			var resultObject = (JObject)sourceObject.DeepClone();
			var transformations = new Stack<ITransformation>();

			transformContext = transformContext ?? new TransformationInvokeContext
			{
				Source = sourceObject,
			};

			Walk(transformationObject, transformations);

			resultObject.Merge(transformationObject, MergeSettings);

			while(transformations.Count > 0)
			{
				var transformation = transformations.Pop();
				transformation.OnError += errorHandler;
				transformation.ApplyTo(resultObject, transformContext);
			}

			return resultObject;
		}

		/// <summary>
		/// Walk around transformation object, and collect all transformations.
		/// </summary>
		/// <param name="token">Transformation object.</param>
		/// <param name="transformations">Stack with transformations.</param>
		private static void Walk(JToken token, Stack<ITransformation> transformations)
		{
			switch (token.Type)
			{
				case JTokenType.Object:
					var properties = ((JObject) token).Properties().ToArray();
					foreach (var property in properties)
					{
						Walk(property, transformations);
					}

					break;
				case JTokenType.Array:
					foreach (var item in (JArray)token)
					{
						Walk(item, transformations);
					}
					break;
				case JTokenType.Property:
					var prop = (JProperty)token;
					var command = new TransformationFactory(Activators).Create(prop);
					if (command != null)
					{
						transformations.Push(command);

						return;
					}

					Walk(prop.Value, transformations);
					break;
			}
		}
	}
}