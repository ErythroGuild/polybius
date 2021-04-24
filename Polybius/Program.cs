﻿using DSharpPlus;		// C# Discord API
using DSharpPlus.Entities;
using HtmlAgilityPack;	// HTTP client + HTML parser

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Polybius {
	class Program {
		private static DiscordClient polybius;
		private static HtmlWeb http;

		private static Dictionary<ulong, Settings> settings;

		private const string path_token = @"config/token.txt";
		private const string url_search = @"https://www.wowdb.com/search?search=";
		private const int color_embed = 0x9A61F1;

		static void Main() {
			const string title_ascii =
				@"     ___      _       _     _           " + "\n" +
				@"    / _ \___ | |_   _| |__ (_)_   _ ___ " + "\n" +
				@"   / /_)/ _ \| | | | | '_ \| | | | / __|" + "\n" +
				@"  / ___/ (_) | | |_| | |_) | | |_| \__ \" + "\n" +
				@"  \/    \___/|_|\__, |_.__/|_|\__,_|___/" + "\n" +
				@"                |___/                   " + "\n";
			Console.WriteLine(title_ascii);
			MainAsync().ConfigureAwait(false).GetAwaiter().GetResult();
		}

		static async Task MainAsync() {
			InitBot();

			polybius.Ready += async (polybius, e) => {
				DiscordActivity helptext =
					new DiscordActivity("@Polybius -help", ActivityType.Watching);
				await polybius.UpdateStatusAsync(helptext);
				Console.WriteLine("Connected to discord servers.");
				Console.WriteLine("Connected to " + polybius.Guilds.Count + " server(s).");
				Console.WriteLine("Monitoring messages...\n");
			};

			// TODO: add undermine journal entry for items
			polybius.MessageCreated += async (polybius, e) => {
				// Never respond to self!
				if (e.Message.Author == polybius.CurrentUser) {
					return;
				}

				// Rate-limit responses to other bots.
				if (e.Message.Author.IsBot) {
					return;	// NYI
				}
				
				List<string> tokens = ExtractTokens(e.Message.Content);
				if (tokens.Count > 0) {
					_ = e.Message.Channel.TriggerTypingAsync();

					// TODO: try/catch search failures
					foreach (string token in tokens) {
						Console.WriteLine("\nToken parsed: " + token);

						List<Entry> entries = SearchDB(token);
						// Courtesy message on 0 results.
						if (entries.Count == 0) {
							Console.WriteLine("> No results found.");
							await e.Message.RespondAsync("No results found for `" + token + "`.");
						}
						foreach (Entry entry in entries) {
							string title = "**" + entry.name + "**";
							// TODO: add clarification for battle pet abilities, etc.

							string tooltip = ExtractTooltip(GetTooltipNode(entry), entry.type);
							tooltip = Sanitizer.SanitizeTooltip(tooltip);
							
							string url_wowhead = GetWowhead(entry);
							string str_details =
								"*Details: " +
								"[WoWDB](" + entry.URL + ") \u2022 " +
								"[Wowhead](" + url_wowhead + ") \u2022 " +
								"[Comments](" + url_wowhead + "#comments)" +
								"*";
							tooltip += "\n\n" + str_details;

							string url_thumbnail = GetThumbnail(entry);
							Console.WriteLine("> thumb: " + url_thumbnail);
							// TODO: do not write this line if no thumbnail

							DiscordEmbed embed = new DiscordEmbedBuilder()
							.WithTitle(title)
							.WithUrl(entry.URL)
							.WithColor(new DiscordColor(color_embed))
							.WithDescription(tooltip)
							.WithThumbnail(url_thumbnail, 80, 80)
							.WithFooter("powered by WoWDB");
							
							await e.Message.RespondAsync(embed);
						}
					}
				}
			};

			await polybius.ConnectAsync();
			await Task.Delay(-1);
		}

		// Init discord client with token from text file.
		// This allows the token to be separated from source code.
		static void InitBot() {
			Console.WriteLine("Initializing Polybius...");
			Console.WriteLine("Reading auth token...");
			string bot_token = "";
			using (StreamReader token = File.OpenText(path_token)) {
				bot_token = token.ReadLine();
			}
			if (bot_token != "")
				Console.WriteLine("Auth token found.");

			polybius = new DiscordClient(new DiscordConfiguration {
				Token = bot_token,
				TokenType = TokenType.Bot
			});

			// Init HtmlAgilityPack parser.
			http = new HtmlWeb();
		}

		// Matches all tokens of the format `[[TOKEN]]`.
		static List<string> ExtractTokens(string msg) {
			string regex_token_str = @"\[\[(.+?)\]\]";

			List<string> tokens = new List<string>();

			Regex regex_token = new Regex(regex_token_str,
				RegexOptions.Compiled | RegexOptions.IgnoreCase);
			MatchCollection matches = regex_token.Matches(msg);

			foreach (Match match in matches) {
				string token = match.Groups[1].Value;
				tokens.Add(token);
			}

			return tokens;
		}

		// Get search results.
		// TODO: search different types of entries one-by-one
		static List<Entry> SearchDB(string q) {
			HtmlDocument doc = http.Load(url_search + q);

			string xpath_tabs =
				@"//div[@id='content']" +
				@"/section[@class='primary-content']" +
				@"//div[@class='b-tab-content tabbed-content']" +
				@"/div/div[@class='b-tab-contentArea']";
			string xpath_entry = "";

			// TODO: error handling
			HtmlNodeCollection nodes_tabs = doc.DocumentNode.SelectNodes(xpath_tabs);
			List<Entry> results = new List<Entry>();
			if (nodes_tabs == null) { return results; }	// TODO: better error handling
			foreach (HtmlNode node_tab in nodes_tabs) {
				Entry.Type type_tab = GetTabEntryType(node_tab);
				if (type_tab != Entry.Type.Unknown) {
					// Must create new node or xpath will search from doc root again.
					HtmlNode node_current = HtmlNode.CreateNode(node_tab.InnerHtml);
					switch (type_tab) {
					case Entry.Type.Spell:
					case Entry.Type.Talent:
					case Entry.Type.Trait:
					case Entry.Type.Mount:
					case Entry.Type.PetSpell:
					case Entry.Type.Item:
						xpath_entry =
							@"//table[contains(@class, 'listing ')]/tbody" +
							@"//table" +
							@"//a[contains(@class, ' t')]";
						break;
					case Entry.Type.Pet:
					case Entry.Type.Currency:
					case Entry.Type.Achieve:
						xpath_entry =
							@"//table[contains(@class, 'listing ')]/tbody" +
							@"//table" +
							@"//a[@class='t']";
						break;
					case Entry.Type.Title:
					case Entry.Type.Quest:
						xpath_entry =
							@"//table[contains(@class, 'listing ')]/tbody" +
							@"/tr/td/a";
						break;
					case Entry.Type.Faction:
						xpath_entry =
							@"//table[contains(@class, 'listing ')]/tbody" +
							@"//a[@class='t']";
						break;
					}
					HtmlNodeCollection nodes_entries = node_current.SelectNodes(xpath_entry);
					if (nodes_entries == null) { break; }
					foreach (HtmlNode node_entry in nodes_entries) {
						Entry entry = new Entry {
							type = type_tab,
							name = node_entry.InnerHtml,
							URL = node_entry.GetAttributeValue("href", ""),
						};
						
						// TODO: separate cleanup into own function
						RegexOptions regex_options = RegexOptions.Compiled | RegexOptions.IgnoreCase;
						const string regex_apos = @"&quot;|&#39;|&#x27;";
						entry.name = Regex.Replace(entry.name, regex_apos, "'", regex_options);
						const string regex_lt = @"&lt;";
						entry.name = Regex.Replace(entry.name, regex_lt, "<", regex_options);
						const string regex_gt = @"&gt;";
						entry.name = Regex.Replace(entry.name, regex_gt, ">", regex_options);
						const string regex_title = @"\s?<Name>,?\s?";
						entry.name = Regex.Replace(entry.name, regex_title, "", regex_options);

						if (entry.name.ToLower() == q.ToLower() && entry.URL != "") {
							Console.WriteLine("> " + entry.name + " - " + entry.URL);
							results.Add(entry);
						}
					}
				}
			}

			return results;
		}

		static Entry.Type GetTabEntryType(HtmlNode node) {
			string id = node.GetAttributeValue("id", "");

			Entry.Type type = Entry.Type.Unknown;
			Dictionary<string, Entry.Type> dict = new Dictionary<string, Entry.Type> {
				{ "tab-abilities",      Entry.Type.Spell },
				{ "tab-racials",        Entry.Type.Spell },
				{ "tab-talents",        Entry.Type.Talent },
				{ "tab-azerite-powers", Entry.Type.Trait },
				{ "tab-mounts",			Entry.Type.Mount },
				{ "tab-battle-pets",	Entry.Type.Pet },
				{ "tab-battle-pet-abilities", Entry.Type.PetSpell },
				{ "tab-items",			Entry.Type.Item },
				{ "tab-currencies",		Entry.Type.Currency },
				{ "tab-titles",			Entry.Type.Title },
				{ "tab-achievements",	Entry.Type.Achieve },
				{ "tab-quests",			Entry.Type.Quest },
				{ "tab-factions",		Entry.Type.Faction },
			};
			if (dict.ContainsKey(id)) {
				type = dict[id];
			}

			return type;
		}

		// TODO: check each page of paginated results
		// (e.g. [[The 2 Ring]])
		static HtmlNode GetTooltipNode(Entry entry) {
			// Navigate to actual entry
			HtmlDocument doc = http.Load(entry.URL);

			string xpath;
			
			switch (entry.type) {
			default:
				xpath =
					@"//div[@id='content']" +
					@"/section[@class='primary-content']/div/section" +
					@"//div[@class='db-tooltip']" +
					@"/div[@class='db-description']";
				break;
			case Entry.Type.Title:
			case Entry.Type.Quest:
			case Entry.Type.Faction:
				xpath =
					@"//div[@id='content']" +
					@"/section[@class='primary-content']/div/section";
				break;
			}

			HtmlNode node = HtmlNode.CreateNode(
				doc.DocumentNode
				.SelectSingleNode(xpath)
				.OuterHtml
			);

			return node;
		}

		static string ExtractTooltip(HtmlNode node, Entry.Type type) {
			string tooltip = "";
			string xpath = "";

			void extract_spell() {
				xpath = @"/*/p";
				tooltip += node.SelectSingleNode(xpath).InnerHtml;
			}
			void extract_mount() {
				xpath = @"/*/i";
				tooltip += node.SelectSingleNode(xpath).InnerHtml;
				tooltip += "\n\n";
				xpath = @"/*/p[2]";
				tooltip += node.SelectSingleNode(xpath).InnerHtml;
			}
			void extract_pet() {
				xpath = @"/*/table//h2";
				string pet_type = node.SelectSingleNode(xpath).GetAttributeValue("data-type", "");
				tooltip += Capitalize(pet_type) + " Battle Pet";
				tooltip += "\n\n";
				xpath = @"/*/p";
				tooltip += node.SelectSingleNode(xpath).InnerHtml;
				tooltip += "\n\n";
				xpath = @"/*/div/p";
				tooltip += node.SelectSingleNode(xpath).InnerHtml;
				tooltip += "\n";
			}
			void extract_petspell() {
				xpath = @"/*/h2";
				string spell_type = node.SelectSingleNode(xpath).GetAttributeValue("data-type", "");
				tooltip += Capitalize(spell_type) + " Battle Pet Ability";
				tooltip += "\n\n";
				// TODO: add cooldown / hit chance info
				xpath = @"/*/p[@class='yellow']";
				tooltip += node.SelectSingleNode(xpath).InnerHtml;
				tooltip += "\n";
			}
			void extract_item() {
				xpath = @"/*/dl";
				tooltip += node.SelectSingleNode(xpath).InnerHtml;
			}
			void extract_plaintext() {
				xpath = @"/*/p";
				tooltip += node.SelectSingleNode(xpath).InnerHtml;
				tooltip += "\n";
			}
			void extract_title() {
				// empty function (skip): titles are self-descriptive
			}
			void extract_quest() {
				xpath = @"/*/p[@class='quest-summary']";
				HtmlNode node_tmp = node.SelectSingleNode(xpath);
				if (node_tmp == null) return;
				tooltip += node_tmp.InnerHtml;
				tooltip += "\n";
			}

			Dictionary<Entry.Type, Action> dict_extract = new Dictionary<Entry.Type, Action> {
				{ Entry.Type.Spell,		extract_spell },
				{ Entry.Type.Talent,	extract_spell },
				{ Entry.Type.Trait,		extract_spell },
				{ Entry.Type.Mount,     extract_mount },
				{ Entry.Type.Pet,		extract_pet },
				{ Entry.Type.PetSpell,	extract_petspell },
				{ Entry.Type.Item,		extract_item },
				{ Entry.Type.Currency,	extract_plaintext },
				{ Entry.Type.Achieve,   extract_plaintext },
				{ Entry.Type.Faction,   extract_plaintext },
				{ Entry.Type.Title,		extract_title },
				{ Entry.Type.Quest,		extract_quest },
			};
			dict_extract[type]();

			Console.WriteLine("> " + tooltip);
			return tooltip;
		}

		static string Capitalize(string s) {
			return s[0].ToString().ToUpper() + s.Substring(1);
		}

		// Each tooltip will have an icon associated with it.
		static string GetThumbnail(Entry entry) {
			// Quests/Factions don't have thumbnails.
			// The entries aren't pulled from tooltip data.
			switch (entry.type) {
			case Entry.Type.Title:
			case Entry.Type.Quest:
			case Entry.Type.Faction:
				return "";
			}

			HtmlDocument doc = http.Load(entry.URL);

			string xpath_tooltip =
				@"//div[@id='content']" +
				@"/section[@class='primary-content']/div/section" +
				@"//div[@class='db-tooltip']";
			string xpath_img = xpath_tooltip +
				@"/div[@class='db-image']" +
				@"/img";

			HtmlNode node_img = doc.DocumentNode.SelectSingleNode(xpath_img);
			string URL = node_img.GetAttributeValue("src", "");
			return URL;
		}

		static string GetWowhead(Entry entry) {
			HtmlDocument doc = http.Load(entry.URL);

			string xpath_sidebar =
				@"//div[@id='content']" +
				@"/section[@class='primary-content']" +
				@"/div/aside[contains(@class, 'infobox')]";
			string xpath_wowhead = xpath_sidebar +
				@"/ul/li[span[@class='wowhead']]" +
				@"/a";

			HtmlNode node_wowhead = doc.DocumentNode.SelectSingleNode(xpath_wowhead);
			string URL = node_wowhead.GetAttributeValue("href", "");
			Console.WriteLine("> wowhead: " + URL);
			return URL;
		}
	}
}
