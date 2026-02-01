using System.Web;

namespace Distillate.Web;

/// <summary>
/// The live-mode pages: an input form with a curl builder, and the result view.
/// Kept out of <c>Program.cs</c> because they are markup and styling rather than
/// routing, and because a 400-line raw string literal in the middle of an endpoint
/// map is the reason that file was hard to read.
/// </summary>
internal static class LiveModePages
{
    internal static string Index(string? errorMessage = null, string? userAgentValue = null, string? refererValue = null, string? cookiesValue = null, bool clearCookies = false, bool pngConvert = false)
    {
        var errorDiv = errorMessage != null
            ? $"""<div class="error">{HttpUtility.HtmlEncode(errorMessage)}</div>"""
            : "";

        var userAgentAttr = HtmlValueAttr(userAgentValue);
        var refererAttr = HtmlValueAttr(refererValue);
        var cookiesContent = HttpUtility.HtmlEncode(cookiesValue ?? "");
        var clearCookiesAttr = clearCookies ? " checked" : "";
        var pngConvertAttr = pngConvert ? " checked" : "";

        return $$"""
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>Distillate — Live Mode</title>
        <style>
            *, *::before, *::after { box-sizing: border-box; }
            body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #1a1a2e; color: #e0e0e0; margin: 0; padding: 2rem; display: flex; justify-content: center; align-items: flex-start; min-height: 100vh; }
            .page-container { max-width: 1100px; width: 100%; background: #16213e; border-radius: 12px; box-shadow: 0 4px 24px rgba(0,0,0,0.4); padding: 2rem; border: 1px solid #0f3460; }
            h1 { margin: 0 0 0.25rem; font-size: 1.6rem; color: #e94560; letter-spacing: 0.02em; }
            p.subtitle { margin: 0 0 1.5rem; color: #8899aa; font-size: 0.9rem; }
            .main-content { display: grid; grid-template-columns: 1fr 1fr; gap: 1.5rem; }
            @media (max-width: 768px) { .main-content { grid-template-columns: 1fr; } }
            form { display: flex; flex-direction: column; gap: 0.75rem; }
            .url-row { display: flex; gap: 0.5rem; }
            input[type="url"], input[type="text"], input[type="number"], textarea, select { padding: 0.6rem 0.75rem; border: 1px solid #0f3460; border-radius: 6px; font-size: 0.9rem; width: 100%; background: #1a1a2e; color: #e0e0e0; transition: border-color 0.2s, box-shadow 0.2s; }
            textarea { font-family: "SF Mono", "Fira Code", "Fira Mono", Menlo, Consolas, monospace; font-size: 0.85rem; resize: vertical; }
            select { cursor: pointer; }
            .url-row input[type="url"] { flex: 1; width: auto; }
            input[type="url"]:focus, input[type="text"]:focus, input[type="number"]:focus, textarea:focus, select:focus { outline: none; border-color: #e94560; box-shadow: 0 0 0 2px rgba(233,69,96,0.25); }
            input::placeholder, textarea::placeholder { color: #556677; }
            button, .extract-btn { padding: 0.6rem 1.5rem; background: #e94560; color: #fff; border: none; border-radius: 6px; font-size: 1rem; font-weight: 600; cursor: pointer; white-space: nowrap; transition: background 0.2s, transform 0.1s; }
            button:hover, .extract-btn:hover { background: #c73652; }
            button:active, .extract-btn:active { transform: scale(0.98); }
            button:disabled { background: #555; cursor: not-allowed; }
            label { font-size: 0.85rem; color: #8899aa; margin-bottom: 0.15rem; display: block; }
            .field { display: flex; flex-direction: column; }
            .checkbox-field { display: flex; align-items: center; gap: 0.5rem; }
            .checkbox-field label { margin-bottom: 0; }
            input[type="checkbox"] { accent-color: #e94560; width: 16px; height: 16px; }
            details { margin-top: 0.25rem; }
            summary { font-size: 0.85rem; color: #8899aa; cursor: pointer; user-select: none; padding: 0.25rem 0; }
            summary:hover { color: #e94560; }
            .optional-fields { display: flex; flex-direction: column; gap: 0.75rem; margin-top: 0.75rem; }
            .error { margin-top: 1rem; padding: 0.75rem; background: rgba(233,69,96,0.12); border: 1px solid rgba(233,69,96,0.35); border-radius: 6px; color: #f5a0b0; font-size: 0.9rem; }
            .curl-section label { font-size: 0.85rem; color: #8899aa; margin-bottom: 0.35rem; display: flex; align-items: center; gap: 0.5rem; }
            .curl-section textarea { background: #111827; color: #9ca3af; border: 1px solid #0f3460; font-size: 0.8rem; line-height: 1.5; }
            .copy-btn { background: transparent; border: 1px solid #0f3460; color: #8899aa; padding: 0.2rem 0.6rem; font-size: 0.75rem; border-radius: 4px; cursor: pointer; font-weight: 400; transition: border-color 0.2s, color 0.2s; }
            .copy-btn:hover { border-color: #e94560; color: #e94560; }
            .curl-options { display: flex; flex-direction: column; gap: 0.35rem; margin: 0.5rem 0; }
            .curl-options .group-label { font-size: 0.7rem; color: #e94560; font-weight: 600; margin-top: 0.5rem; text-transform: uppercase; letter-spacing: 0.05em; }
            .curl-options .checkbox-field { font-size: 0.8rem; }
            .curl-options .checkbox-field label { font-size: 0.8rem; }
            .curl-options input[type="checkbox"] { width: 14px; height: 14px; }
            .curl-options .field { gap: 0.2rem; }
            .curl-options .field label { font-size: 0.8rem; }
            .curl-options .field input, .curl-options .field select { font-size: 0.8rem; padding: 0.35rem 0.5rem; }
            .separator { border: none; border-top: 1px solid #0f3460; margin: 0.5rem 0 0; }
            .console-section { margin-top: 1.5rem; }
            .console-section > label { font-size: 0.85rem; color: #8899aa; margin-bottom: 0.35rem; display: flex; align-items: center; gap: 0.5rem; }
            .console-output { background: #0d1117; border: 1px solid #0f3460; border-radius: 6px; padding: 0.75rem; font-family: "SF Mono", "Fira Code", "Fira Mono", Menlo, Consolas, monospace; font-size: 0.8rem; line-height: 1.6; min-height: 120px; max-height: 300px; overflow-y: auto; color: #9ca3af; }
            .console-output .log-line { margin: 0; }
            .console-output .log-error { color: #e94560; }
            .console-output .log-success { color: #4ade80; }
        </style>
    </head>
    <body>
        <div class="page-container">
            <h1>Distillate</h1>
            <p class="subtitle">Enter a URL to extract its reader view with embedded images.</p>
            <div class="main-content">
                <div class="left-column">
                    <form method="get" action="/live" id="distillateForm">
                        <div class="url-row">
                            <input type="url" id="url" name="url" placeholder="https://example.com/article" required autofocus />
                            <button type="submit" id="extractBtn">Extract</button>
                        </div>
                        <details>
                            <summary>Advanced options</summary>
                            <div class="optional-fields">
                                <div class="field">
                                    <label for="userAgent">User-Agent (leave blank for default)</label>
                                    <input type="text" id="userAgent" name="userAgent" placeholder="Mozilla/5.0 …"{{userAgentAttr}}/>
                                </div>
                                <div class="field">
                                    <label for="referer">Referer (leave blank for none)</label>
                                    <input type="text" id="referer" name="referer" placeholder="https://example.com/"{{refererAttr}}/>
                                </div>
                                <div class="field">
                                    <label for="cookies">Cookies (one per line: name=value)</label>
                                    <textarea id="cookies" name="cookies" rows="3" placeholder="session=abc123&#10;token=xyz789">{{cookiesContent}}</textarea>
                                </div>
                                <hr class="separator" />
                                <div class="checkbox-field">
                                    <input type="checkbox" id="clearCookies" name="clearCookies" value="on"{{clearCookiesAttr}}/>
                                    <label for="clearCookies">Clear cookies (disable cookie storage for this request)</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="pngConvert" name="pngConvert" value="on"{{pngConvertAttr}}/>
                                    <label for="pngConvert">Convert images to PNG</label>
                                </div>
                            </div>
                        </details>
                    </form>
                    {{errorDiv}}
                </div>
                <div class="right-column">
                    <div class="curl-section">
                        <label for="curlOutput">Equivalent REST API curl command <button type="button" class="copy-btn" onclick="navigator.clipboard.writeText(document.getElementById('curlOutput').value)">Copy</button></label>
                        <details>
                            <summary>Curl-only options (all REST API fields)</summary>
                            <div class="curl-options">
                                <div class="group-label">Content Outputs</div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeSourceHtml" />
                                    <label for="curl_includeSourceHtml">includeSourceHtml</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeCleanHtmlEmbeddedImages" />
                                    <label for="curl_includeCleanHtmlEmbeddedImages">includeCleanHtmlEmbeddedImages</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeCleanHtmlLinkedImages" />
                                    <label for="curl_includeCleanHtmlLinkedImages">includeCleanHtmlLinkedImages</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeCleanHtmlNoImages" />
                                    <label for="curl_includeCleanHtmlNoImages">includeCleanHtmlNoImages</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeMarkdownLinkedImages" />
                                    <label for="curl_includeMarkdownLinkedImages">includeMarkdownLinkedImages</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeMarkdownNoImages" checked />
                                    <label for="curl_includeMarkdownNoImages">includeMarkdownNoImages</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includePlainText" checked />
                                    <label for="curl_includePlainText">includePlainText</label>
                                </div>
                                <div class="group-label">Link Extraction</div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeAllLinksInPage" />
                                    <label for="curl_includeAllLinksInPage">includeAllLinksInPage</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeAllLinksMedia" />
                                    <label for="curl_includeAllLinksMedia">includeAllLinksMedia</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeAllLinksAssets" />
                                    <label for="curl_includeAllLinksAssets">includeAllLinksAssets</label>
                                </div>
                                <div class="group-label">Image Extraction</div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeAllInPageImages" />
                                    <label for="curl_includeAllInPageImages">includeAllInPageImages</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeAllOutOfPageImages" />
                                    <label for="curl_includeAllOutOfPageImages">includeAllOutOfPageImages</label>
                                </div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_imageLinkRewrite" />
                                    <label for="curl_imageLinkRewrite">imageLinkRewrite</label>
                                </div>
                                <div class="group-label">Processing</div>
                                <div class="field">
                                    <label for="curl_engine">engine</label>
                                    <input type="text" id="curl_engine" value="smartreader" />
                                </div>
                                <div class="field">
                                    <label for="curl_timeoutMs">timeoutMs</label>
                                    <input type="number" id="curl_timeoutMs" placeholder="e.g. 60000" />
                                </div>
                                <div class="field">
                                    <label for="curl_stabilizationMs">stabilizationMs</label>
                                    <input type="number" id="curl_stabilizationMs" placeholder="e.g. 5000" />
                                </div>
                                <div class="field">
                                    <label for="curl_unicodeNormalization">unicodeNormalization</label>
                                    <select id="curl_unicodeNormalization">
                                        <option value="preserve" selected>preserve</option>
                                        <option value="ascii">ascii</option>
                                        <option value="latex">latex</option>
                                    </select>
                                </div>
                                <div class="group-label">Debug</div>
                                <div class="checkbox-field">
                                    <input type="checkbox" id="curl_includeDebugInfo" />
                                    <label for="curl_includeDebugInfo">includeDebugInfo</label>
                                </div>
                            </div>
                        </details>
                        <textarea id="curlOutput" rows="10" readonly></textarea>
                    </div>
                </div>
            </div>
            <div class="console-section" id="consoleSection">
                <label>Console Output <button type="button" class="copy-btn" onclick="document.getElementById('consoleOutput').innerHTML=''">Clear</button></label>
                <div id="consoleOutput" class="console-output"></div>
            </div>
        </div>
        <script>
        (function() {
            var STORAGE_KEY = 'distillate-settings';

            function saveSettings() {
                var settings = {
                    url: document.getElementById('url').value,
                    userAgent: document.getElementById('userAgent').value,
                    referer: document.getElementById('referer').value,
                    cookies: document.getElementById('cookies').value,
                    clearCookies: document.getElementById('clearCookies').checked,
                    pngConvert: document.getElementById('pngConvert').checked
                };
                try { localStorage.setItem(STORAGE_KEY, JSON.stringify(settings)); } catch(e) {}
            }

            function loadSettings() {
                if (document.querySelector('.error')) return;
                try {
                    var saved = localStorage.getItem(STORAGE_KEY);
                    if (!saved) return;
                    var s = JSON.parse(saved);
                    var fields = ['url', 'userAgent', 'referer'];
                    for (var i = 0; i < fields.length; i++) {
                        var el = document.getElementById(fields[i]);
                        if (el && !el.value && s[fields[i]]) el.value = s[fields[i]];
                    }
                    var cookies = document.getElementById('cookies');
                    if (cookies && !cookies.value && s.cookies) cookies.value = s.cookies;
                    if (s.clearCookies) document.getElementById('clearCookies').checked = true;
                    if (s.pngConvert) document.getElementById('pngConvert').checked = true;
                } catch(e) {}
            }

            loadSettings();

            var contentFlags = {
                includeSourceHtml:              { responseField: 'sourceHtml',              file: 'source.html' },
                includeCleanHtmlEmbeddedImages: { responseField: 'cleanHtmlEmbeddedImages', file: 'article-embedded.html' },
                includeCleanHtmlLinkedImages:    { responseField: 'cleanHtmlLinkedImages',   file: 'article-linked.html' },
                includeCleanHtmlNoImages:        { responseField: 'cleanHtmlNoImages',       file: 'article-no-images.html' },
                includeMarkdownLinkedImages:     { responseField: 'markdownLinkedImages',    file: 'article-linked.md' },
                includeMarkdownNoImages:         { responseField: 'markdownNoImages',        file: 'article.md' },
                includePlainText:               { responseField: 'plainText',               file: 'article.txt' }
            };

            function buildCurl() {
                var url = document.getElementById('url').value.trim();
                var ua  = document.getElementById('userAgent').value.trim();
                var ref = document.getElementById('referer').value.trim();
                var ck  = document.getElementById('cookies').value.trim();
                var cc  = document.getElementById('clearCookies').checked;
                var png = document.getElementById('pngConvert').checked;

                var body = { url: url || 'https://example.com/article' };
                if (ua)  body.userAgent = ua;
                if (ref) body.referer = ref;
                if (cc)  body.clearCookies = true;
                if (png) body.pngConversionRequired = true;
                if (ck) {
                    var cookies = ck.split('\n').map(function(line) {
                        var eq = line.indexOf('=');
                        if (eq <= 0) return null;
                        return { name: line.substring(0, eq).trim(), value: line.substring(eq + 1).trim() };
                    }).filter(Boolean);
                    if (cookies.length > 0) body.cookies = cookies;
                }

                var engine = document.getElementById('curl_engine').value.trim();
                if (engine && engine !== 'smartreader') body.engine = engine;
                var timeout = document.getElementById('curl_timeoutMs').value.trim();
                if (timeout) body.timeoutMs = parseInt(timeout);
                var stabilization = document.getElementById('curl_stabilizationMs').value.trim();
                if (stabilization) body.stabilizationMs = parseInt(stabilization);
                var unicode = document.getElementById('curl_unicodeNormalization').value;
                if (unicode !== 'preserve') body.unicodeNormalization = unicode;

                var selectedOutputs = [];
                for (var key in contentFlags) {
                    var el = document.getElementById('curl_' + key);
                    if (el) {
                        body[key] = el.checked;
                        if (el.checked) selectedOutputs.push(contentFlags[key]);
                    }
                }

                var linkFlags = ['includeAllLinksInPage', 'includeAllLinksMedia', 'includeAllLinksAssets'];
                for (var i = 0; i < linkFlags.length; i++) {
                    var el = document.getElementById('curl_' + linkFlags[i]);
                    if (el && el.checked) body[linkFlags[i]] = true;
                }

                var imageFlags = ['includeAllInPageImages', 'includeAllOutOfPageImages', 'imageLinkRewrite'];
                for (var i = 0; i < imageFlags.length; i++) {
                    var el = document.getElementById('curl_' + imageFlags[i]);
                    if (el && el.checked) body[imageFlags[i]] = true;
                }

                var debug = document.getElementById('curl_includeDebugInfo');
                if (debug && debug.checked) body.includeDebugInfo = true;

                var json = JSON.stringify(body, null, 2);
                var cmd = "curl -X POST http://localhost:8080/process \\\n  -H \"Content-Type: application/json\" \\\n  -d '" + json + "'";

                if (selectedOutputs.length > 0) {
                    cmd += " \\\n  -o response.json";
                    cmd += "\n\n# Save individual outputs to files:";
                    for (var i = 0; i < selectedOutputs.length; i++) {
                        cmd += "\njq -r '." + selectedOutputs[i].responseField + "' response.json > " + selectedOutputs[i].file;
                    }
                }

                document.getElementById('curlOutput').value = cmd;
            }

            ['url','userAgent','referer','cookies'].forEach(function(id) {
                document.getElementById(id).addEventListener('input', buildCurl);
            });
            ['clearCookies','pngConvert'].forEach(function(id) {
                document.getElementById(id).addEventListener('change', buildCurl);
            });
            var curlInputs = document.querySelectorAll('.curl-options input, .curl-options select');
            for (var i = 0; i < curlInputs.length; i++) {
                curlInputs[i].addEventListener(curlInputs[i].type === 'checkbox' ? 'change' : 'input', buildCurl);
            }

            buildCurl();

            function logMessage(msg, cls) {
                var con = document.getElementById('consoleOutput');
                var line = document.createElement('div');
                line.className = 'log-line' + (cls ? ' ' + cls : '');
                line.textContent = msg;
                con.appendChild(line);
                con.scrollTop = con.scrollHeight;
            }

            var form = document.getElementById('distillateForm');
            form.addEventListener('submit', function(e) {
                e.preventDefault();
                saveSettings();
                document.getElementById('consoleOutput').innerHTML = '';
                var ts = new Date().toISOString().substring(11, 23);
                logMessage('[' + ts + '] Starting extraction...');

                var formData = new FormData(form);
                var params = new URLSearchParams(formData).toString();
                var btn = document.getElementById('extractBtn');
                btn.disabled = true;
                btn.textContent = 'Processing…';

                var done = false;
                var source = new EventSource('/live/events?' + params);

                source.addEventListener('log', function(e) {
                    logMessage(e.data);
                });

                source.addEventListener('fail', function(e) {
                    done = true;
                    logMessage('ERROR: ' + e.data, 'log-error');
                    source.close();
                    btn.disabled = false;
                    btn.textContent = 'Extract';
                });

                source.addEventListener('result', function(e) {
                    done = true;
                    logMessage('[' + new Date().toISOString().substring(11, 23) + '] Loading result…', 'log-success');
                    source.close();
                    try {
                        var html = atob(e.data);
                        document.open();
                        document.write(html);
                        document.close();
                    } catch(err) {
                        logMessage('ERROR: Failed to load result — ' + err.message, 'log-error');
                        btn.disabled = false;
                        btn.textContent = 'Extract';
                    }
                });

                source.onerror = function() {
                    if (!done) {
                        logMessage('[' + new Date().toISOString().substring(11, 23) + '] Connection error', 'log-error');
                    }
                    source.close();
                    btn.disabled = false;
                    btn.textContent = 'Extract';
                };
            });
        })();
        </script>
    </body>
    </html>
    """;
    }

    internal static string Result(string title, string articleHtml, string sourceUrl, double processingTimeMs)
    {
        return $$"""
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>{{title}}</title>
        <style>
            *, *::before, *::after { box-sizing: border-box; }
            body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #1a1a2e; color: #e0e0e0; margin: 0; padding: 0; }
            .toolbar { background: #16213e; border-bottom: 1px solid #0f3460; padding: 0.75rem 2rem; display: flex; align-items: center; gap: 1rem; flex-wrap: wrap; position: sticky; top: 0; z-index: 10; }
            .toolbar a { color: #e94560; text-decoration: none; font-size: 0.9rem; transition: color 0.2s; }
            .toolbar a:hover { color: #f5a0b0; text-decoration: underline; }
            .toolbar .meta { margin-left: auto; color: #556677; font-size: 0.8rem; }
            .article { max-width: 800px; margin: 2rem auto; background: #16213e; border-radius: 12px; box-shadow: 0 4px 24px rgba(0,0,0,0.4); padding: 2rem; border: 1px solid #0f3460; }
            .article img { max-width: 100%; height: auto; border-radius: 6px; }
            .article a { color: #e94560; }
            .article a:hover { color: #f5a0b0; }
        </style>
    </head>
    <body>
        <div class="toolbar">
            <a href="/">&#8592; New extraction</a>
            <a href="{{HttpUtility.HtmlAttributeEncode(sourceUrl)}}" target="_blank" rel="noopener">Original page &#8599;</a>
            <span class="meta">{{processingTimeMs:F0}} ms</span>
        </div>
        <div class="article">
            {{articleHtml}}
        </div>
    </body>
    </html>
    """;
    }

    // Returns a ` value="..."` HTML attribute string for the given value, or an empty string when value is null.
    private static string HtmlValueAttr(string? value) =>
        value != null ? $""" value="{HttpUtility.HtmlAttributeEncode(value)}" """ : "";

}
