using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreePackages {
	internal static class Steam {
		internal sealed class PlaytestAccessResponse {
			[JsonInclude]
			[JsonPropertyName("granted")]
			[JsonRequired]
			internal int? Granted  { get; private init; } = null;

			[JsonInclude]
			[JsonPropertyName("success")]
			[JsonRequired]
			internal int Success  { get; private init; } = 0;

			[JsonConstructor]
			internal PlaytestAccessResponse() {}
		}

		internal sealed class UserData {
			[JsonInclude]
			[JsonPropertyName("rgOwnedPackages")]
			[JsonRequired]
			internal HashSet<uint> OwnedPackages { get; private init; } = new();

			[JsonInclude]
			[JsonPropertyName("rgOwnedApps")]
			[JsonRequired]
			internal HashSet<uint> OwnedApps { get; private init; } = new();

			[JsonInclude]
			[JsonPropertyName("rgIgnoredApps")]
			[JsonRequired]
			[JsonConverter(typeof(EmptyArrayOrDictionaryConverter))]
			internal Dictionary<uint, uint> IgnoredApps { get; private init; } = new();

			[JsonInclude]
			[JsonPropertyName("rgExcludedTags")]
			[JsonRequired]
			internal List<Tag> ExcludedTags { get; private init; } = new();

			[JsonInclude]
			[JsonPropertyName("rgExcludedContentDescriptorIDs")]
			[JsonRequired]
			internal HashSet<uint> ExcludedContentDescriptorIDs { get; private init; } = new();

			[JsonInclude]
			[JsonPropertyName("rgWishlist")]
			[JsonRequired]
			internal HashSet<uint> WishlistedApps { get; private init; } = new();

			[JsonInclude]
			[JsonPropertyName("rgFollowedApps")]
			[JsonRequired]
			internal HashSet<uint> FollowedApps { get; private init; } = new();

			[JsonExtensionData]
			[JsonInclude]
			internal Dictionary<string, JsonElement> AdditionalData { get; private init; } = new();

			[JsonConstructor]
			internal UserData() {}
		}

		internal sealed class Tag {
			[JsonInclude]
			[JsonPropertyName("tagid")]
			[JsonRequired]
			internal uint TagID = 0;

			[JsonInclude]
			[JsonPropertyName("name")]
			[JsonRequired]
			internal string Name = "";

			[JsonInclude]
			[JsonPropertyName("timestamp_added")]
			[JsonRequired]
			internal uint TimestampAdded = 0;

			[JsonConstructor]
			internal Tag() {}
		}

		internal sealed class UserInfo {
			[JsonInclude]
			[JsonPropertyName("logged_in")]
			public bool LoggedIn;

			[JsonInclude]
			[JsonPropertyName("steamid")]
			public string SteamID = "";

			[JsonInclude]
			[JsonPropertyName("accountid")]
			public int AccountID;

			[JsonInclude]
			[JsonPropertyName("account_name")]
			public string AccountName = "";

			[JsonInclude]
			[JsonPropertyName("is_support")]
			public bool IsSupport;

			[JsonInclude]
			[JsonPropertyName("is_limited")]
			public bool IsLimited;

			[JsonInclude]
			[JsonPropertyName("is_partner_member")]
			public bool IsPartnerMember;

			[JsonInclude]
			[JsonPropertyName("is_valve_email")]
			public bool IsValveEmail;

			[JsonInclude]
			[JsonPropertyName("country_code")]
			[JsonRequired]
			public string CountryCode = "";

			[JsonInclude]
			[JsonPropertyName("excluded_content_descriptors")]
			public HashSet<uint> ExcludedContentDescriptors = new();

			[JsonConstructor]
			internal UserInfo() {}
		}

		// https://stackoverflow.com/questions/12221950/how-to-deserialize-object-that-can-be-an-array-or-a-dictionary-with-newtonsoft
		public class EmptyArrayOrDictionaryConverter : JsonConverter<Dictionary<uint, uint>> {
			public override Dictionary<uint, uint> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
				if (reader.TokenType == JsonTokenType.StartObject) {
					var dictionary = JsonSerializer.Deserialize<Dictionary<uint, uint>>(ref reader, options);
					if (dictionary == null) {
						throw new JsonException();
					}

					return dictionary;
				} else if (reader.TokenType == JsonTokenType.StartArray) {
					reader.Read();
					if (reader.TokenType == JsonTokenType.EndArray) {
						return new Dictionary<uint, uint>();
					}
				}

				throw new JsonException();
			}

			public override void Write(Utf8JsonWriter writer, Dictionary<uint, uint> value, JsonSerializerOptions options) {
				throw new NotImplementedException();
			}
		}
	}
}
