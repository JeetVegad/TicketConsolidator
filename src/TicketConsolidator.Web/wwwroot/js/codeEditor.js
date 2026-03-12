window.codeEditor = {
    init: function (textareaId, overlayId) {
        var txt = document.getElementById(textareaId);
        var pre = document.getElementById(overlayId);

        if (!txt || !pre) return;

        function update() {
            var code = txt.value;
            // Escape HTML
            var html = code
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;");

            // Token storage to avoid recursive regex matching
            var tokens = [];
            function addToken(str) {
                tokens.push(str);
                return "###TOKEN" + (tokens.length - 1) + "###";
            }

            // 1. Comments
            html = html.replace(/(&lt;!--[\s\S]*?--&gt;)/g, function (m) {
                return addToken('<span class="token-comment">' + m + '</span>');
            });

            // 2. Tag Names (Opening & Closing)
            // Match &lt;div or &lt;/div
            html = html.replace(/(&lt;\/?[a-zA-Z0-9]+)/g, function (m) {
                return addToken('<span class="token-tag">' + m + '</span>');
            });

            // 3. Tag Ends
            html = html.replace(/(&gt;)/g, function (m) {
                return addToken('<span class="token-tag">' + m + '</span>');
            });

            // 4. Attribute Values (Strings)
            html = html.replace(/("[^"]*")/g, function (m) {
                return addToken('<span class="token-attr-value">' + m + '</span>');
            });

            // 5. Attributes (Keys)
            // Looking for space + word + =
            html = html.replace(/(\s)([a-zA-Z0-9\-]+)(=)/g, function (m, s, name, eq) {
                return s + addToken('<span class="token-attr-name">' + name + '</span>') + eq;
            });

            // Restore Tokens
            tokens.forEach(function (val, idx) {
                // Use split/join for faster replacement
                html = html.split("###TOKEN" + idx + "###").join(val);
            });

            // Handle trailing newline
            if (code.slice(-1) === "\n") {
                html += "<br/>";
            }

            pre.innerHTML = html;
        }

        // Sync scroll
        txt.addEventListener("scroll", function () {
            pre.scrollTop = txt.scrollTop;
            pre.scrollLeft = txt.scrollLeft;
        });

        // Update on input
        txt.addEventListener("input", update);

        // Initial update
        update();

        txt.updateHighlight = update;
    },

    // Call this if value changes externally (e.g. Reset)
    refresh: function (textareaId) {
        var txt = document.getElementById(textareaId);
        if (txt && txt.updateHighlight) {
            txt.updateHighlight();
        }
    }
};
