using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FreePackages {
	internal sealed class UserData {
		[JsonProperty(PropertyName = "rgOwnedPackages", Required = Required.Always)]
		internal HashSet<uint> OwnedPackages = new();

		[JsonProperty(PropertyName = "rgOwnedApps", Required = Required.Always)]
		internal HashSet<uint> OwnedApps = new();

		[JsonProperty(PropertyName = "rgIgnoredApps", Required = Required.Always)]
		[JsonConverter(typeof(EmptyArrayOrDictionaryConverter))]
		internal Dictionary<uint, uint> IgnoredApps = new();

		[JsonProperty(PropertyName = "rgExcludedTags", Required = Required.Always)]
		internal List<Tag> ExcludedTags = new();

		[JsonProperty(PropertyName = "rgExcludedContentDescriptorIDs", Required = Required.Always)]
		internal HashSet<uint> ExcludedContentDescriptorIDs = new();

		[JsonProperty(PropertyName = "rgWishlist", Required = Required.Always)]
		internal HashSet<uint> WishlistedApps = new();

		[JsonProperty(PropertyName = "rgFollowedApps", Required = Required.Always)]
		internal HashSet<uint> FollowedApps = new();

		[JsonExtensionData]
		internal Dictionary<string, JToken> AdditionalData = new();

		[JsonConstructor]
		internal UserData() {}
	}

	internal sealed class Tag {
		[JsonProperty(PropertyName = "tagid", Required = Required.Always)]
		internal uint TagID = 0;

		[JsonProperty(PropertyName = "name", Required = Required.Always)]
		internal string Name = "";

		[JsonProperty(PropertyName = "timestamp_added", Required = Required.Always)]
		internal uint TimestampAdded = 0;

		[JsonConstructor]
		internal Tag() {}
	}

	// https://stackoverflow.com/questions/12221950/how-to-deserialize-object-that-can-be-an-array-or-a-dictionary-with-newtonsoft
	public class EmptyArrayOrDictionaryConverter : JsonConverter {
		public override bool CanConvert(Type objectType) {
			return objectType.IsAssignableFrom(typeof(Dictionary<string, object>));
		}

		public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) {
			JToken token = JToken.Load(reader);
			if (token.Type == JTokenType.Object) {
				return token.ToObject(objectType, serializer);
			} else if (token.Type == JTokenType.Array) {
				if (!token.HasValues) {
					// create empty dictionary
					return Activator.CreateInstance(objectType);
				}
			}

			throw new JsonSerializationException("Object or empty array expected");
		}

		public override bool CanWrite {
			get { return false; }
		}
			
		public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) {
			throw new NotImplementedException();
		}
	}
}