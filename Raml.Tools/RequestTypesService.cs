using System.Collections.Generic;
using System.Linq;
using Raml.Parser.Expressions;
using Raml.Tools.Pluralization;

namespace Raml.Tools
{
    public class RequestTypesService
    {
        private readonly IDictionary<string, ApiObject> schemaObjects;
        private readonly IDictionary<string, ApiObject> schemaRequestObjects;
        private readonly IDictionary<string, string> linkKeysWithObjectNames;
        private readonly IEnumerable<IDictionary<string, ResourceType>> resourceTypes;

        private readonly SchemaParameterParser schemaParameterParser = new SchemaParameterParser(new EnglishPluralizationService());

        public RequestTypesService(IDictionary<string, ApiObject> schemaObjects, IDictionary<string, ApiObject> schemaRequestObjects, 
            IDictionary<string, string> linkKeysWithObjectNames, IEnumerable<IDictionary<string, ResourceType>> resourceTypes)
        {
            this.schemaObjects = schemaObjects;
            this.schemaRequestObjects = schemaRequestObjects;
            this.linkKeysWithObjectNames = linkKeysWithObjectNames;
            this.resourceTypes = resourceTypes;
        }

        public ApiObject GetRequestApiObject(string key, Method method, Resource resource, string fullUrl, string defaultMediaType)
        {
            var schema = GetJsonSchemaOrDefault(method.Body, defaultMediaType);
            if (schema != null)
            {
                var apiObject = GetRequestApiObjectWhenNamed(method, resource, schema, fullUrl);
                if (apiObject != null)
                    return apiObject;
            }

            if (resource.Type != null && resource.Type.Any() &&
                resourceTypes.Any(rt => rt.ContainsKey(resource.GetSingleType())))
            {
                var verb = RamlTypesHelper.GetResourceTypeVerb(method, resource, resourceTypes);
                if (verb != null && verb.Body != null && !string.IsNullOrWhiteSpace(verb.Body.Schema))
                {
                    var apiObject = GetRequestApiObjectWhenNamed(method, resource, verb.Body.Schema, fullUrl);
                    if (apiObject != null)
                        return apiObject;
                }

                if (verb != null && verb.Body != null && !string.IsNullOrWhiteSpace(verb.Body.Type))
                {
                    var apiObject = GetRequestApiObjectWhenNamed(method, resource, verb.Body.Type, fullUrl);
                    if (apiObject != null)
                        return apiObject;
                }
            }

            var apiObjectByKey = GetRequestApiObjectByKey(key);
            if (apiObjectByKey != null)
                return apiObjectByKey;


            var requestKey = key + GeneratorServiceBase.RequestContentSuffix;
            apiObjectByKey = GetRequestApiObjectByKey(requestKey);
            if (apiObjectByKey != null)
                return apiObjectByKey;

            if (linkKeysWithObjectNames.ContainsKey(key))
            {
                var linkedKey = linkKeysWithObjectNames[key];
                apiObjectByKey = GetRequestApiObjectByKey(linkedKey);
                if (apiObjectByKey != null)
                    return apiObjectByKey;
            }

            if (linkKeysWithObjectNames.ContainsKey(requestKey))
            {
                var linkedKey = linkKeysWithObjectNames[requestKey];
                apiObjectByKey = GetRequestApiObjectByKey(linkedKey);
                if (apiObjectByKey != null)
                    return apiObjectByKey;
            }

            return null;
        }

        public string DecodeRequestRaml1Type(string type)
        {
            // TODO: can I handle this better ?
            if (type.Contains("(") || type.Contains("|"))
                return "string";

            if (type.EndsWith("[][]")) // array of arrays
                return CollectionTypeHelper.GetCollectionType(CollectionTypeHelper.GetCollectionType(type.Substring(0, type.Length - 4)));

            if (type.EndsWith("[]")) // array
                return CollectionTypeHelper.GetCollectionType(type.Substring(0, type.Length - 2));

            if (type.EndsWith("{}")) // Map
            {
                var subtype = type.Substring(0, type.Length - 2);
                var netType = NetTypeMapper.Map(subtype);
                if (!string.IsNullOrWhiteSpace(netType))
                    return "IDictionary<string, " + netType + ">";

                var apiObject = GetRequestApiObjectByName(subtype);
                if (apiObject != null)
                    return "IDictionary<string, " + RamlTypesHelper.GetTypeFromApiObject(apiObject) + ">";

                return "IDictionary<string, object>";
            }

            return "string";
        }

        private ApiObject GetRequestApiObjectByKey(string key)
        {
            if (!RequestHasKey(key))
                return null;

            return schemaObjects.ContainsKey(key) ? schemaObjects[key] : schemaRequestObjects[key];
        }

        private bool RequestHasKey(string key)
        {
            return schemaObjects.ContainsKey(key) || schemaRequestObjects.ContainsKey(key);
        }

        private ApiObject GetRequestApiObjectWhenNamed(Method method, Resource resource, string type, string fullUrl)
        {
            if (type.Contains("<<") && type.Contains(">>"))
                return GetRequestApiObjectByParametrizedName(method, resource, type, fullUrl);

            if (!type.Contains("<") && !type.Contains("{"))
                return GetRequestApiObjectByName(type);

            return null;
        }

        private ApiObject GetRequestApiObjectByName(string schema)
        {
            var type = schema.ToLowerInvariant();

            if (schemaObjects.Values.Any(o => o.Name.ToLowerInvariant() == type))
                return schemaObjects.Values.First(o => o.Name.ToLowerInvariant() == type);

            if (schemaRequestObjects.Values.Any(o => o.Name.ToLowerInvariant() == type))
                return schemaRequestObjects.Values.First(o => o.Name.ToLowerInvariant() == type);

            return null;
        }

        private ApiObject GetRequestApiObjectByParametrizedName(Method method, Resource resource, string schema, string fullUrl)
        {
            var type = schemaParameterParser.Parse(schema, resource, method, fullUrl);

            if (schemaObjects.Any(o => o.Key.ToLower() == type.ToLowerInvariant()))
                return schemaObjects.First(o => o.Key.ToLower() == type.ToLowerInvariant()).Value;

            if (schemaObjects.Values.Any(o => o.Name.ToLower() == type.ToLowerInvariant()))
                return schemaObjects.Values.First(o => o.Name.ToLower() == type.ToLowerInvariant());

            if (schemaRequestObjects.Any(o => o.Key.ToLower() == type.ToLowerInvariant()))
                return schemaRequestObjects.First(o => o.Key.ToLower() == type.ToLowerInvariant()).Value;

            if (schemaRequestObjects.Values.Any(o => o.Name.ToLower() == type.ToLowerInvariant()))
                return schemaRequestObjects.Values.First(o => o.Name.ToLower() == type.ToLowerInvariant());

            return null;
        }

        private string GetJsonSchemaOrDefault(IDictionary<string, MimeType> body, string defaultMediaType)
        {
            if (body.Any(b => b.Key.ToLowerInvariant().Contains("json") && b.Value != null
                              && (!string.IsNullOrWhiteSpace(b.Value.Schema)
                                  || !string.IsNullOrWhiteSpace(b.Value.Type))))
            {
                if (body.Any(b => b.Key.ToLowerInvariant().Contains("json") && b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Schema)))
                    return body.First(b => b.Key.ToLowerInvariant().Contains("json") && b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Schema)).Value.Schema;

                return body.First(b => b.Key.ToLowerInvariant().Contains("json") && b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Type)).Value.Type;
            }

            var mediaType = defaultMediaType != null ? defaultMediaType.ToLowerInvariant() : string.Empty;

            var isDefaultMediaTypeDefined = !string.IsNullOrWhiteSpace(defaultMediaType);
            var hasSchemaWithDefaultMediaType = body.Any(b => b.Key.ToLowerInvariant() == mediaType
                                                              && b.Value != null &&
                                                              (!string.IsNullOrWhiteSpace(b.Value.Schema) ||
                                                               !string.IsNullOrWhiteSpace(b.Value.Type)));

            if (isDefaultMediaTypeDefined && hasSchemaWithDefaultMediaType)
            {
                if (body.Any(b => b.Key.ToLowerInvariant() == mediaType
                                  && b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Schema)))
                {
                    return body.First(b => b.Key.ToLowerInvariant() == mediaType
                                           && b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Schema)).Value.Schema;
                }

                return body.First(b => b.Key.ToLowerInvariant() == mediaType
                                       && b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Type)).Value.Type;
            }

            if (body.Any(b => b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Schema)))
                return body.First(b => b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Schema)).Value.Schema;

            if (body.Any(b => b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Type)))
                return body.First(b => b.Value != null && !string.IsNullOrWhiteSpace(b.Value.Schema)).Value.Type;

            return null;
        }

    }
}