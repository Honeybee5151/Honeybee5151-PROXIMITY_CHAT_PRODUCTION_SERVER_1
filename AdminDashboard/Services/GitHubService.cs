using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AdminDashboard.Services
{
    public class GitHubService
    {
        private readonly HttpClient _http;
        private readonly string _repo; // "owner/repo"
        private readonly string _branch;

        public GitHubService()
        {
            var pat = Environment.GetEnvironmentVariable("GITHUB_PAT") ?? "";
            _repo = Environment.GetEnvironmentVariable("GITHUB_REPO") ?? "";
            _branch = Environment.GetEnvironmentVariable("GITHUB_BRANCH") ?? "master";

            _http = new HttpClient();
            _http.BaseAddress = new Uri("https://api.github.com/");
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("AdminDashboard/1.0");
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");

            if (!string.IsNullOrEmpty(pat))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", pat);

            if (string.IsNullOrEmpty(_repo) || string.IsNullOrEmpty(pat))
                Console.WriteLine("[GitHubService] WARNING: GITHUB_PAT or GITHUB_REPO not set");
            else
                Console.WriteLine($"[GitHubService] Configured for {_repo} ({_branch})");
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_repo) &&
            _http.DefaultRequestHeaders.Authorization != null;

        /// <summary>Fetch a file's content (UTF-8) and its SHA from the repo</summary>
        public async Task<(string Content, string Sha)> FetchFile(string path)
        {
            var res = await _http.GetAsync($"repos/{_repo}/contents/{path}?ref={_branch}");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception($"GitHub fetch error for {path}: {res.StatusCode} - {err}");
            }
            var data = JObject.Parse(await res.Content.ReadAsStringAsync());
            var base64 = data["content"]?.ToString().Replace("\n", "") ?? "";
            var content = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).TrimStart('\uFEFF');
            var sha = data["sha"]?.ToString() ?? "";
            return (content, sha);
        }

        /// <summary>List files in a directory from the repo</summary>
        public async Task<List<string>> ListDirectory(string path)
        {
            var res = await _http.GetAsync($"repos/{_repo}/contents/{path}?ref={_branch}");
            if (!res.IsSuccessStatusCode)
                return new List<string>();
            var items = JArray.Parse(await res.Content.ReadAsStringAsync());
            return items.Where(i => i["type"]?.ToString() == "file")
                        .Select(i => i["path"]?.ToString())
                        .Where(p => p != null)
                        .ToList();
        }

        /// <summary>Fetch a binary file's raw bytes from the repo</summary>
        public async Task<byte[]> FetchBinaryFile(string path)
        {
            var res = await _http.GetAsync($"repos/{_repo}/contents/{path}?ref={_branch}");
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception($"GitHub fetch error for {path}: {res.StatusCode} - {err}");
            }
            var data = JObject.Parse(await res.Content.ReadAsStringAsync());
            var base64 = data["content"]?.ToString().Replace("\n", "") ?? "";
            return Convert.FromBase64String(base64);
        }

        /// <summary>
        /// Commit multiple files atomically using the Git Trees API.
        /// Creates blobs → tree → commit → updates branch ref.
        /// </summary>
        public async Task CommitFiles(
            List<(string Path, string Content)> files,
            string message,
            List<(string Path, byte[] Content)> binaryFiles = null)
        {
            // 1. Get HEAD commit SHA
            var refRes = await _http.GetAsync($"repos/{_repo}/git/ref/heads/{_branch}");
            refRes.EnsureSuccessStatusCode();
            var refData = JObject.Parse(await refRes.Content.ReadAsStringAsync());
            var headSha = refData["object"]["sha"].ToString();

            // 2. Get base tree SHA
            var commitRes = await _http.GetAsync($"repos/{_repo}/git/commits/{headSha}");
            commitRes.EnsureSuccessStatusCode();
            var commitData = JObject.Parse(await commitRes.Content.ReadAsStringAsync());
            var baseTreeSha = commitData["tree"]["sha"].ToString();

            // 3. Create blobs and build tree entries
            var treeEntries = new JArray();
            foreach (var file in files)
            {
                var blobPayload = new JObject
                {
                    ["content"] = file.Content,
                    ["encoding"] = "utf-8"
                };
                var blobRes = await Post($"repos/{_repo}/git/blobs", blobPayload);
                var blobSha = blobRes["sha"].ToString();

                treeEntries.Add(new JObject
                {
                    ["path"] = file.Path,
                    ["mode"] = "100644",
                    ["type"] = "blob",
                    ["sha"] = blobSha
                });
            }

            // 3b. Create blobs for binary files
            if (binaryFiles != null)
            {
                foreach (var file in binaryFiles)
                {
                    var blobPayload = new JObject
                    {
                        ["content"] = Convert.ToBase64String(file.Content),
                        ["encoding"] = "base64"
                    };
                    var blobRes = await Post($"repos/{_repo}/git/blobs", blobPayload);
                    var blobSha = blobRes["sha"].ToString();

                    treeEntries.Add(new JObject
                    {
                        ["path"] = file.Path,
                        ["mode"] = "100644",
                        ["type"] = "blob",
                        ["sha"] = blobSha
                    });
                }
            }

            // 4. Create new tree
            var treePayload = new JObject
            {
                ["base_tree"] = baseTreeSha,
                ["tree"] = treeEntries
            };
            var treeRes = await Post($"repos/{_repo}/git/trees", treePayload);
            var newTreeSha = treeRes["sha"].ToString();

            // 5. Create commit
            var commitPayload = new JObject
            {
                ["message"] = message,
                ["tree"] = newTreeSha,
                ["parents"] = new JArray(headSha)
            };
            var newCommitRes = await Post($"repos/{_repo}/git/commits", commitPayload);
            var newCommitSha = newCommitRes["sha"].ToString();

            // 6. Update branch ref
            var updatePayload = new JObject { ["sha"] = newCommitSha };
            var patchContent = new StringContent(updatePayload.ToString(), Encoding.UTF8, "application/json");
            var patchRes = await _http.PatchAsync($"repos/{_repo}/git/refs/heads/{_branch}", patchContent);
            if (!patchRes.IsSuccessStatusCode)
            {
                var err = await patchRes.Content.ReadAsStringAsync();
                throw new Exception($"GitHub ref update error: {patchRes.StatusCode} - {err}");
            }

            var totalFiles = files.Count + (binaryFiles?.Count ?? 0);
            Console.WriteLine($"[GitHubService] Committed {totalFiles} file(s): {message} (sha: {newCommitSha})");

            // Verify the commit is accessible on GitHub before returning
            // This prevents Coolify webhook from cloning before GitHub has settled
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var verifyRes = await _http.GetAsync($"repos/{_repo}/git/commits/{newCommitSha}");
                if (verifyRes.IsSuccessStatusCode) break;
                await Task.Delay(1000);
            }
        }

        private async Task<JObject> Post(string url, JObject payload)
        {
            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var res = await _http.PostAsync(url, content);
            if (!res.IsSuccessStatusCode)
            {
                var err = await res.Content.ReadAsStringAsync();
                throw new Exception($"GitHub API error ({url}): {res.StatusCode} - {err}");
            }
            return JObject.Parse(await res.Content.ReadAsStringAsync());
        }
    }
}
