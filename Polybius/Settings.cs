﻿using DSharpPlus;
using DSharpPlus.Entities;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Polybius {
	class Settings {
		public const string group_query = "query";
		public const string group_meta = "meta";

		private const string path_save_base = "config/guild-";
		private const string path_save_file = "settings.txt";

		public ulong id;
		public bool do_log_stats;
		public string token_L;
		public string token_R;
		public string split;
		public ulong? ch_bot;
		public HashSet<ulong> ch_whitelist;
		public HashSet<ulong> ch_blacklist;

		// Default constructor:
		// stat logging, [[query|meta]] tokens, no bot channel
		public Settings(ulong id) {
			this.id = id;
			do_log_stats = true;
			token_L = "[["; split = "|"; token_R = "]]";
			ch_bot = null;
			ch_whitelist = new HashSet<ulong>();
			ch_blacklist = new HashSet<ulong>();
		}

		public Regex regex_token() {
			// e.g.:
			// \Q[[\E(?<query>.+?)(?:\Q|\E(?<meta>.+?))?\Q]]\E
			string regex_str =
				@"\Q" + token_L + @"\E" +
				@"(?<"+ group_query + @">.+?)" +
				@"(?:\Q" + split + @"\E" +
				@"(?<" + group_meta + @">.+?))?" +
				@"\Q" + token_R + @"\E";
			return new Regex(regex_str,
				RegexOptions.Compiled | RegexOptions.IgnoreCase);
		}

		public bool is_ch_safe(ulong id) {
			if (ch_whitelist.Count > 0) {
				if (!ch_whitelist.Contains(id)) {
					// a whitelist exists and the channel is not on it
					return false;
				} else if (ch_blacklist.Contains(id)) {
					// a whitelist exists and the channel is on it, &&
					// the channel is also on the blacklist
					return false;
				} else {
					// a whitelist exists and the channel is on it, &&
					// the channel is not on the blacklist
					return true;
				}
			} else if (!ch_blacklist.Contains(id)) {
				// a whitelist does not exist, &&
				// the channel is not on the blacklist
				return true;
			} else {
				// a whitelist does not exist, &&
				// the channel is on the blacklist
				return false;
			}
		}

		private const string delim_key = ":";
		private const string delim_entry = ",";
		private const string str_null = "null";
		private const string key_log_stats = "do_log_stats";
		private const string key_token_L = "token_L";
		private const string key_token_R = "token_R";
		private const string key_split = "split";
		private const string key_ch_bot = "ch_bot";
		private const string key_ch_whitelist = "ch_whitelist";
		private const string key_ch_blacklist = "ch_blacklist";

		private string get_path_save() {
			return path_save_base + id.ToString() + "/" + path_save_file;
		}

		public void save() {
			StreamWriter file_save = new StreamWriter(get_path_save());

			// Convenience functions for writing to the file.
			void SaveVal(string key, string val) {
				file_save.WriteLine(key + delim_key + val);
			}
			void SaveVals(string key, List<string> vals) {
				string val = "";
				foreach (string entry in vals)
					{ val += entry + delim_entry; }
				// trim the trailing delimiter
				val = val.Remove(val.LastIndexOf(delim_entry));
				SaveVal(key, val);
			}

			SaveVal(key_log_stats, do_log_stats.ToString());
			SaveVal(key_token_L, token_L);
			SaveVal(key_token_R, token_R);
			SaveVal(key_split, split);

			// `null` is a special case that is easily disambiguated on read,
			// since otherwise a `ulong` will only have digits after conversion.
			string str_ch_bot = ch_bot?.ToString() ?? str_null;
			SaveVal(key_ch_bot, str_ch_bot);

			List<string> vals_whitelist = new List<string>();
			foreach (ulong ch in ch_whitelist)
				{ vals_whitelist.Add(ch.ToString()); }
			SaveVals(key_ch_whitelist, vals_whitelist);

			List<string> vals_blacklist = new List<string>();
			foreach (ulong ch in ch_blacklist)
				{ vals_blacklist.Add(ch.ToString()); }
			SaveVals(key_ch_blacklist, vals_blacklist);

			// Flush/finalize the save file.
			file_save.Close();
		}

		public static Settings load(ulong id) {
			Settings settings = new Settings(id);
			StreamReader file_save =
				new StreamReader(settings.get_path_save());

			// Read in the file line-by-line.
			while (!file_save.EndOfStream) {
				string line = file_save.ReadLine();
				string[] line_split = line.Split(delim_key, 2);
				string key = line_split[0];
				string val = line_split[1];

				switch (key) {
				case key_log_stats:
					settings.do_log_stats = Convert.ToBoolean(val);
					break;
				case key_token_L:
					settings.token_L = val;
					break;
				case key_token_R:
					settings.token_R = val;
					break;
				case key_split:
					settings.split = val;
					break;

				case key_ch_bot:
					if (val == str_null)
						{ settings.ch_bot = null; }
					else
						{ settings.ch_bot = Convert.ToUInt64(val); }
					break;

				case key_ch_whitelist:
					string[] vals_whitelist = val.Split(delim_entry);
					if (vals_whitelist[0] != "") {
						foreach (string entry in vals_whitelist) {
							settings.ch_whitelist.Add(Convert.ToUInt64(entry));
						}
					}
					break;
				case key_ch_blacklist:
					string[] vals_blacklist = val.Split(delim_entry);
					if (vals_blacklist[0] != "") {
						foreach (string entry in vals_blacklist) {
							settings.ch_whitelist.Add(Convert.ToUInt64(entry));
						}
					}
					break;
				}
			}

			file_save.Close();
			return settings;
		}
	}
}