// ==UserScript==
// @name         NetDownloader 1-Click Fast Resolver (FitGirl & FuckingFast)
// @namespace    https://github.com/NetDownloader
// @version      4.2
// @description  1-Click direct resolver for FitGirl Pastebin and FuckingFast. Automatically resolves all parts and sends direct CDN links with preserved filenames to NetDownloader.
// @author       NetDownloader
// @match        https://paste.fitgirl-repacks.site/*
// @match        https://*.fitgirl-repacks.site/*
// @match        https://fitgirl-repacks.site/*
// @match        https://fuckingfast.co/*
// @match        https://*.fuckingfast.co/*
// @grant        GM_xmlhttpRequest
// @grant        GM_setValue
// @grant        GM_getValue
// @grant        GM_setClipboard
// @grant        window.close
// @connect      127.0.0.1
// @connect      localhost
// @connect      fuckingfast.co
// @connect      *.fuckingfast.co
// @connect      *
// @run-at       document-idle
// ==/UserScript==

(function () {
    'use strict';

    const NETDOWNLOADER_PORTS = [9666, 9667, 9668, 9669];

    // Helper: Send payload to NetDownloader on port 9666/9667
    async function sendToNetDownloader(urls, packageName) {
        const payload = JSON.stringify({
            packageName: packageName || 'NetDownloader_Package',
            urls: Array.isArray(urls) ? urls : [urls]
        });

        for (const port of NETDOWNLOADER_PORTS) {
            const endpoint = `http://127.0.0.1:${port}/api/add`;
            try {
                const success = await sendHttp(endpoint, payload);
                if (success) {
                    showToast(`✅ Successfully queued ${urls.length} link(s) in NetDownloader!`, false);
                    return true;
                }
            } catch (e) { }
        }

        copyToClipboard(urls.join('\n'));
        showToast('⚠️ NetDownloader app not responding on port 9666/9667. Direct links copied to clipboard!', true);
        return false;
    }

    function sendHttp(url, jsonBody) {
        return new Promise((resolve) => {
            if (typeof GM_xmlhttpRequest !== 'undefined') {
                GM_xmlhttpRequest({
                    method: 'POST',
                    url: url,
                    headers: { 'Content-Type': 'application/json' },
                    data: jsonBody,
                    timeout: 3000,
                    onload: function (resp) {
                        resolve(resp.status >= 200 && resp.status < 300);
                    },
                    onerror: function () { resolve(false); },
                    ontimeout: function () { resolve(false); }
                });
            } else {
                fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: jsonBody
                })
                .then(r => resolve(r.ok))
                .catch(() => resolve(false));
            }
        });
    }

    function copyToClipboard(text) {
        if (typeof GM_setClipboard !== 'undefined') {
            GM_setClipboard(text, 'text');
        } else if (navigator.clipboard) {
            navigator.clipboard.writeText(text).catch(() => {});
        }
    }

    function showToast(message, isError = false, duration = 4000) {
        let toast = document.getElementById('nd-toast-notification');
        if (!toast) {
            toast = document.createElement('div');
            toast.id = 'nd-toast-notification';
            toast.style.position = 'fixed';
            toast.style.bottom = '24px';
            toast.style.right = '24px';
            toast.style.zIndex = '2147483647';
            toast.style.padding = '14px 22px';
            toast.style.borderRadius = '10px';
            toast.style.fontFamily = 'Segoe UI, system-ui, sans-serif';
            toast.style.fontSize = '14px';
            toast.style.fontWeight = '600';
            toast.style.color = '#FFFFFF';
            toast.style.boxShadow = '0 10px 30px rgba(0,0,0,0.5)';
            toast.style.transition = 'all 0.3s cubic-bezier(0.16, 1, 0.3, 1)';
            document.body.appendChild(toast);
        }
        toast.textContent = message;
        toast.style.background = isError ? '#EF4444' : '#10B981';
        toast.style.display = 'block';
        toast.style.opacity = '1';

        setTimeout(() => {
            if (toast) {
                toast.style.opacity = '0';
                setTimeout(() => { if (toast) toast.style.display = 'none'; }, 300);
            }
        }, duration);
    }

    // Helper: Direct resolution via GM_xmlhttpRequest (Cross-Origin, zero tab opening!)
    function resolveViaGmHttp(id) {
        return new Promise((resolve) => {
            GM_xmlhttpRequest({
                method: 'POST',
                url: `https://fuckingfast.co/f/${id}/go`,
                headers: {
                    'HX-Request': 'true',
                    'User-Agent': navigator.userAgent,
                    'Referer': `https://fuckingfast.co/${id}`
                },
                timeout: 7000,
                onload: function (resp) {
                    let dlUrl = null;

                    // 1. Inspect response headers for HX-Redirect
                    const headers = resp.responseHeaders || '';
                    const mHeader = headers.match(/hx-redirect:\s*([^\r\n]+)/i);
                    if (mHeader) {
                        dlUrl = mHeader[1].trim();
                    }

                    // 2. Check finalUrl if followed redirect
                    if (!dlUrl && resp.finalUrl && resp.finalUrl.includes('/dl/')) {
                        dlUrl = resp.finalUrl;
                    }

                    // 3. Check responseText
                    if (!dlUrl && resp.responseText) {
                        const mBody = resp.responseText.match(/https?:\/\/[^\s"'<>]+(?:\/dl\/[^\s"'<>]+|\.rar|\.zip|\.7z)/i);
                        if (mBody) dlUrl = mBody[0];
                    }

                    if (dlUrl) {
                        resolve({ success: true, url: dlUrl });
                    } else if (resp.status === 403 || resp.responseText?.includes('Just a moment') || resp.responseText?.includes('challenge-running')) {
                        resolve({ success: false, cfBlocked: true });
                    } else {
                        trySecondEndpoint();
                    }
                },
                onerror: function () { trySecondEndpoint(); },
                ontimeout: function () { trySecondEndpoint(); }
            });

            function trySecondEndpoint() {
                GM_xmlhttpRequest({
                    method: 'POST',
                    url: `https://fuckingfast.co/${id}`,
                    headers: {
                        'HX-Request': 'true',
                        'User-Agent': navigator.userAgent,
                        'Referer': `https://fuckingfast.co/${id}`
                    },
                    timeout: 7000,
                    onload: function (resp) {
                        const headers = resp.responseHeaders || '';
                        const mHeader = headers.match(/hx-redirect:\s*([^\r\n]+)/i);
                        let dlUrl = mHeader ? mHeader[1].trim() : null;
                        if (!dlUrl && resp.finalUrl && resp.finalUrl.includes('/dl/')) {
                            dlUrl = resp.finalUrl;
                        }
                        if (!dlUrl && resp.responseText) {
                            const mBody = resp.responseText.match(/https?:\/\/[^\s"'<>]+(?:\/dl\/[^\s"'<>]+|\.rar|\.zip|\.7z)/i);
                            if (mBody) dlUrl = mBody[0];
                        }
                        if (dlUrl) {
                            resolve({ success: true, url: dlUrl });
                        } else {
                            resolve({ success: false });
                        }
                    },
                    onerror: function () { resolve({ success: false }); },
                    ontimeout: function () { resolve({ success: false }); }
                });
            }
        });
    }

    // =========================================================================
    // 1. Logic on fuckingfast.co
    // =========================================================================
    if (window.location.hostname.includes('fuckingfast.co')) {
        console.log('[NetDownloader] Active on fuckingfast.co');

        function waitForPageReady(callback) {
            const check = () => {
                const title = document.title || '';
                // Only wait if interstitial Cloudflare challenge page is actively showing
                if (title.includes('Just a moment') && !document.body?.innerText?.includes('FUCKINGFAST')) {
                    setTimeout(check, 400);
                    return;
                }
                callback();
            };
            check();
        }

        async function resolveInBrowserTab(id) {
            try {
                // Try /f/{id}/go
                let res = await fetch(`/f/${id}/go`, {
                    method: 'POST',
                    headers: { 'HX-Request': 'true' },
                    credentials: 'include'
                });

                let dlUrl = res.headers.get('hx-redirect') || res.headers.get('HX-Redirect');

                if (!dlUrl) {
                    res = await fetch(`/${id}`, {
                        method: 'POST',
                        headers: { 'HX-Request': 'true' },
                        credentials: 'include'
                    });
                    dlUrl = res.headers.get('hx-redirect') || res.headers.get('HX-Redirect');
                }

                if (!dlUrl && res.url && res.url.includes('/dl/')) {
                    dlUrl = res.url;
                }

                const contentType = res.headers.get('content-type') || '';
                if (!dlUrl && (contentType.includes('text') || contentType.includes('json') || contentType.includes('html'))) {
                    const text = await res.text();
                    const m = text.match(/https?:\/\/[^\s"'<>]+(?:\/dl\/[^\s"'<>]+|\.rar|\.zip|\.7z)/i);
                    if (m) dlUrl = m[0];
                }

                if (dlUrl) {
                    return new URL(dlUrl, window.location.origin).href;
                }
            } catch (err) {
                console.error('[NetDownloader] Error resolving ID:', id, err);
            }
            return null;
        }

        waitForPageReady(async () => {
            // A. Check if opened with #nd_batch in URL hash (Worker Tab Mode)
            const hash = window.location.hash || '';
            let batchData = null;

            if (hash.includes('nd_batch=')) {
                try {
                    const jsonStr = decodeURIComponent(hash.split('nd_batch=')[1]);
                    batchData = JSON.parse(jsonStr);
                } catch (e) {
                    console.error('[NetDownloader] Error parsing nd_batch from hash:', e);
                }
            }

            // Fallback check storage
            if (!batchData && typeof GM_getValue !== 'undefined') {
                const q = GM_getValue('netdownloader_batch_queue', null);
                if (q && Array.isArray(q) && q.length > 0) {
                    batchData = {
                        queue: q,
                        pkg: GM_getValue('netdownloader_package_name', 'Bloodborne_FitGirl')
                    };
                    GM_setValue('netdownloader_batch_queue', null);
                }
            }

            if (batchData && batchData.queue && batchData.queue.length > 0) {
                const queue = batchData.queue;
                const pkgName = batchData.pkg || 'Bloodborne_FitGirl';

                // Display Active Worker Banner
                const banner = document.createElement('div');
                banner.id = 'nd-worker-banner';
                banner.style.position = 'fixed';
                banner.style.top = '0';
                banner.style.left = '0';
                banner.style.right = '0';
                banner.style.background = 'linear-gradient(90deg, #1E232F, #131722)';
                banner.style.borderBottom = '2px solid #2D8CFF';
                banner.style.color = '#FFFFFF';
                banner.style.padding = '16px 24px';
                banner.style.zIndex = '2147483647';
                banner.style.boxShadow = '0 8px 30px rgba(0,0,0,0.7)';
                banner.style.fontFamily = 'Segoe UI, system-ui, sans-serif';
                banner.innerHTML = `
                    <div style="display:flex; justify-content:space-between; align-items:center;">
                        <div>
                            <div style="font-weight:bold; font-size:16px; color:#2D8CFF;">⚡ NetDownloader Auto-Worker</div>
                            <div id="nd-worker-status" style="font-size:13px; color:#94A3B8; margin-top:4px;">Resolving ${queue.length} parts in background...</div>
                        </div>
                        <div id="nd-worker-counter" style="font-size:22px; font-weight:bold; color:#10B981;">0 / ${queue.length}</div>
                    </div>
                `;
                document.body.appendChild(banner);

                const directLinks = [];
                const statusEl = document.getElementById('nd-worker-status');
                const counterEl = document.getElementById('nd-worker-counter');

                for (let i = 0; i < queue.length; i++) {
                    const link = queue[i];
                    const match = link.match(/fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)/);
                    if (!match) continue;

                    const id = match[1];
                    if (statusEl) statusEl.textContent = `Resolving Part ${i + 1} of ${queue.length} (ID: ${id})...`;

                    let direct = await resolveInBrowserTab(id);
                    if (direct) {
                        if (link.includes('#') && !direct.includes('#')) {
                            direct += link.substring(link.indexOf('#'));
                        }
                        directLinks.push(direct);
                    } else {
                        directLinks.push(link);
                    }

                    if (counterEl) counterEl.textContent = `${i + 1} / ${queue.length}`;
                    await new Promise(r => setTimeout(r, 300));
                }

                if (directLinks.length > 0) {
                    if (statusEl) statusEl.textContent = `Sending ${directLinks.length} parts directly to NetDownloader...`;
                    await sendToNetDownloader(directLinks, pkgName);
                    copyToClipboard(directLinks.join('\n'));

                    if (statusEl) statusEl.innerHTML = `<span style="color:#10B981; font-weight:bold;">🎉 All ${directLinks.length} parts sent to NetDownloader! Closing tab in 2s...</span>`;
                    setTimeout(() => window.close(), 2200);
                }
                return;
            }

            // B. Single File View Mode on fuckingfast.co
            const pathMatch = window.location.pathname.match(/\/(?:f\/)?([a-zA-Z0-9]+)/);
            if (pathMatch && pathMatch[1] && pathMatch[1] !== 'speed' && pathMatch[1] !== 'index' && pathMatch[1] !== 'help') {
                const fileId = pathMatch[1];

                const singleCard = document.createElement('div');
                singleCard.id = 'nd-single-card';
                singleCard.style.position = 'fixed';
                singleCard.style.bottom = '24px';
                singleCard.style.right = '24px';
                singleCard.style.zIndex = '2147483647';
                singleCard.style.background = '#181C24';
                singleCard.style.border = '1px solid #2D8CFF';
                singleCard.style.borderRadius = '12px';
                singleCard.style.padding = '14px 18px';
                singleCard.style.boxShadow = '0 10px 30px rgba(0,0,0,0.6)';
                singleCard.style.fontFamily = 'Segoe UI, system-ui, sans-serif';
                singleCard.style.color = '#FFFFFF';
                singleCard.style.minWidth = '280px';

                const fileName = window.location.hash.replace(/^#/, '') || document.querySelector('h1, h2')?.textContent?.trim() || fileId;

                singleCard.innerHTML = `
                    <div style="font-weight:bold; font-size:14px; color:#2D8CFF; margin-bottom:4px;">⚡ NetDownloader</div>
                    <div style="font-size:12px; color:#94A3B8; margin-bottom:12px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; max-width:260px;" title="${fileName}">
                        ${fileName}
                    </div>
                    <div style="display:flex; gap:8px;">
                        <button id="nd-single-send" style="flex:1; background:#2D8CFF; color:#FFFFFF; border:none; border-radius:6px; padding:10px 12px; font-weight:bold; font-size:12px; cursor:pointer;">
                            🚀 Send to NetDownloader
                        </button>
                        <button id="nd-single-copy" style="background:#222733; color:#CBD5E1; border:1px solid #334155; border-radius:6px; padding:10px 10px; font-size:12px; cursor:pointer;" title="Copy direct link">
                            📋 Copy
                        </button>
                    </div>
                    <div style="margin-top:8px;">
                        <button id="nd-single-batch" style="width:100%; background:#1E2533; color:#2D8CFF; border:1px solid #2D8CFF; border-radius:6px; padding:7px 10px; font-weight:600; font-size:11px; cursor:pointer;">
                            ⚡ Paste &amp; Resolve All Parts
                        </button>
                    </div>
                `;

                document.body.appendChild(singleCard);

                const sendBtn = document.getElementById('nd-single-send');
                const copyBtn = document.getElementById('nd-single-copy');
                const batchBtn = document.getElementById('nd-single-batch');

                if (batchBtn) {
                    batchBtn.onclick = async () => {
                        const raw = prompt('Paste all part links (from the pastebin) here:');
                        if (!raw) return;
                        const links = raw.match(/https?:\/\/[^\s"'<>]+/gi) || [];
                        if (links.length === 0) {
                            showToast('No valid links found in pasted text.', true);
                            return;
                        }
                        batchBtn.disabled = true;
                        batchBtn.textContent = `Resolving ${links.length} parts...`;
                        const directList = [];
                        for (let i = 0; i < links.length; i++) {
                            const l = links[i];
                            const m = l.match(/fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)/);
                            if (m) {
                                batchBtn.textContent = `Resolving ${i + 1}/${links.length}...`;
                                let d = await resolveInBrowserTab(m[1]);
                                if (d) {
                                    if (l.includes('#') && !d.includes('#')) {
                                        d += l.substring(l.indexOf('#'));
                                    }
                                    directList.push(d);
                                } else {
                                    directList.push(l);
                                }
                            } else {
                                directList.push(l);
                            }
                            await new Promise(r => setTimeout(r, 250));
                        }
                        let pkgName = fileName;
                        for (const item of directList) {
                            if (item.includes('#')) {
                                let hash = item.split('#')[1];
                                if (hash) {
                                    let cleaned = hash.replace(/[\._\- ]+part\d+[\._\- ]*(?:\.rar|\.zip|\.7z|\.001)?$/i, '');
                                    cleaned = cleaned.replace(/\.(?:rar|zip|7z|001)$/i, '');
                                    if (cleaned.length > 2) {
                                        pkgName = cleaned;
                                        break;
                                    }
                                }
                            }
                        }
                        await sendToNetDownloader(directList, pkgName);
                        copyToClipboard(directList.join('\n'));
                        batchBtn.textContent = '✅ All parts queued!';
                        setTimeout(() => { batchBtn.textContent = '⚡ Paste & Resolve All Parts'; batchBtn.disabled = false; }, 3000);
                    };
                }

                let cachedDirectUrl = null;

                async function getDirectUrl() {
                    if (cachedDirectUrl) return cachedDirectUrl;
                    sendBtn.textContent = 'Resolving...';
                    cachedDirectUrl = await resolveInBrowserTab(fileId);
                    return cachedDirectUrl;
                }

                sendBtn.onclick = async () => {
                    sendBtn.disabled = true;
                    let url = await getDirectUrl();
                    if (url) {
                        if (window.location.hash && !url.includes('#')) {
                            url += window.location.hash;
                        } else if (fileName && !url.includes('#') && fileName !== fileId) {
                            url += '#' + fileName;
                        }
                        await sendToNetDownloader([url], fileName);
                        sendBtn.textContent = '✅ Sent!';
                        setTimeout(() => { sendBtn.textContent = '🚀 Send to NetDownloader'; sendBtn.disabled = false; }, 3000);
                    } else {
                        showToast('Could not extract direct link.', true);
                        sendBtn.textContent = '🚀 Send to NetDownloader';
                        sendBtn.disabled = false;
                    }
                };

                copyBtn.onclick = async () => {
                    copyBtn.disabled = true;
                    let url = await getDirectUrl();
                    if (url) {
                        if (window.location.hash && !url.includes('#')) {
                            url += window.location.hash;
                        } else if (fileName && !url.includes('#') && fileName !== fileId) {
                            url += '#' + fileName;
                        }
                        copyToClipboard(url);
                        showToast('📋 Copied direct link to clipboard!');
                    }
                    copyBtn.disabled = false;
                };
            }
        });
    }

    // =========================================================================
    // 2. Logic on paste.fitgirl-repacks.site & fitgirl-repacks.site
    // =========================================================================
    if (window.location.hostname.includes('fitgirl-repacks.site')) {
        console.log('[NetDownloader] Active on FitGirl site');

        function getLinksIfDecrypted() {
            // If on PrivateBin paste page:
            if (window.location.hostname.includes('paste.fitgirl-repacks.site')) {
                // Check if PrivateBin is still decrypting
                const statusEl = document.getElementById('status');
                if (statusEl && statusEl.textContent && statusEl.textContent.includes('Decrypting')) {
                    return []; // Still decrypting - DO NOT touch DOM or trigger reflow!
                }

                // In PrivateBin, cleartext is in #cleartext or #prettyprint
                const cleartextEl = document.getElementById('cleartext') || 
                                    document.getElementById('prettyprint') ||
                                    document.querySelector('#cleartext, pre.prettyprint');

                if (!cleartextEl) return [];
                const text = cleartextEl.textContent || '';
                if (text.trim().length === 0) return [];

                return parseLinksFromTextAndAnchors(text, cleartextEl);
            } else {
                // On main FitGirl repack blog:
                const container = document.querySelector('.entry-content, #content, main') || document.body;
                if (!container) return [];
                return parseLinksFromTextAndAnchors(container.textContent || '', container);
            }
        }

        function parseLinksFromTextAndAnchors(text, container) {
            const urlRegex = /(?:https?:\/\/)?(?:[a-zA-Z0-9-]+\.)?fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)(?:#[^\s"'<>]*)?/gi;
            const matches = text.match(urlRegex) || [];

            const anchorHrefs = Array.from(container.querySelectorAll('a'))
                .map(a => a.href)
                .filter(h => h && h.includes('fuckingfast.co/'));

            const combined = [...matches, ...anchorHrefs];
            return Array.from(new Set(combined.map(url => {
                let u = url.trim().replace(/[.,;'">\)\]]+$/, '');
                if (!u.startsWith('http://') && !u.startsWith('https://')) {
                    u = 'https://' + u;
                }
                return u;
            })));
        }

        function derivePackageName(links) {
            for (const link of links) {
                if (link.includes('#')) {
                    const hash = link.split('#')[1];
                    if (hash) {
                        let cleaned = hash.replace(/[\._\- ]+part\d+[\._\- ]*(?:\.rar|\.zip|\.7z|\.001)?$/i, '');
                        cleaned = cleaned.replace(/\.(?:rar|zip|7z|001)$/i, '');
                        if (cleaned.length > 2) return cleaned;
                    }
                }
            }
            return document.title.replace(/[\-\|].*$/, '').trim() || 'FitGirl_Repack';
        }

        function createOrUpdateUI() {
            const links = getLinksIfDecrypted();
            if (links.length === 0) return;

            if (document.getElementById('nd-fitgirl-card')) {
                const countBadge = document.getElementById('nd-parts-count');
                if (countBadge) countBadge.textContent = `${links.length} parts found`;
                return;
            }

            const pkgName = derivePackageName(links);

            const card = document.createElement('div');
            card.id = 'nd-fitgirl-card';
            card.style.position = 'fixed';
            card.style.bottom = '24px';
            card.style.right = '24px';
            card.style.zIndex = '2147483647';
            card.style.background = '#181C24';
            card.style.border = '1px solid #2D8CFF';
            card.style.borderRadius = '12px';
            card.style.padding = '16px 20px';
            card.style.boxShadow = '0 12px 36px rgba(0,0,0,0.6)';
            card.style.fontFamily = 'Segoe UI, system-ui, sans-serif';
            card.style.color = '#FFFFFF';
            card.style.minWidth = '320px';
            card.style.maxWidth = '420px';

            card.innerHTML = `
                <div style="display:flex; justify-content:space-between; align-items:center; margin-bottom:8px;">
                    <div style="font-weight:700; font-size:15px; color:#2D8CFF; display:flex; align-items:center; gap:6px;">
                        <span>⚡</span> NetDownloader
                    </div>
                    <span id="nd-parts-count" style="background:#222733; color:#10B981; font-size:12px; font-weight:600; padding:2px 8px; border-radius:6px; border:1px solid #10B981;">
                        ${links.length} parts found
                    </span>
                </div>
                <div style="font-size:12px; color:#94A3B8; margin-bottom:12px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap;" title="${pkgName}">
                    ${pkgName}
                </div>
                <div style="display:flex; gap:8px;">
                    <button id="nd-btn-resolve" style="flex:1; background:#2D8CFF; color:#FFFFFF; border:none; border-radius:8px; padding:10px 14px; font-weight:bold; font-size:13px; cursor:pointer;">
                        🚀 1-Click Resolve &amp; Download
                    </button>
                    <button id="nd-btn-copy" style="background:#222733; color:#CBD5E1; border:1px solid #334155; border-radius:8px; padding:10px 12px; font-weight:600; font-size:12px; cursor:pointer;" title="Copy all raw links">
                        📋 Copy
                    </button>
                </div>
                <div id="nd-card-status" style="font-size:11px; color:#64748B; margin-top:8px; text-align:center;">
                    Resolves direct CDN links automatically
                </div>
            `;

            document.body.appendChild(card);

            const resolveBtn = document.getElementById('nd-btn-resolve');
            const copyBtn = document.getElementById('nd-btn-copy');
            const statusEl = document.getElementById('nd-card-status');

            copyBtn.onclick = () => {
                const currentLinks = getLinksIfDecrypted();
                copyToClipboard(currentLinks.join('\n'));
                showToast(`📋 Copied ${currentLinks.length} links to clipboard!`);
            };

            resolveBtn.onclick = async () => {
                const currentLinks = getLinksIfDecrypted();
                if (currentLinks.length === 0) {
                    showToast('No links found to resolve.', true);
                    return;
                }

                resolveBtn.disabled = true;
                resolveBtn.style.opacity = '0.6';

                // STEP 1: Attempt in-page resolution via GM_xmlhttpRequest (Zero tabs opened!)
                const directLinks = [];
                let cfBlocked = false;

                for (let i = 0; i < currentLinks.length; i++) {
                    const link = currentLinks[i];
                    const idMatch = link.match(/fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)/);
                    if (!idMatch) continue;

                    const id = idMatch[1];
                    if (statusEl) statusEl.textContent = `Resolving Part ${i + 1} of ${currentLinks.length}...`;

                    const res = await resolveViaGmHttp(id);
                    if (res && res.success && res.url) {
                        let directUrl = res.url;
                        if (link.includes('#') && !directUrl.includes('#')) {
                            directUrl += link.substring(link.indexOf('#'));
                        }
                        directLinks.push(directUrl);
                        console.log(`[NetDownloader] Part ${i + 1} resolved directly:`, directUrl);
                    } else if (res && res.cfBlocked) {
                        console.warn('[NetDownloader] Cloudflare challenge detected on Part', i + 1);
                        cfBlocked = true;
                        break;
                    } else {
                        directLinks.push(link);
                    }

                    await new Promise(r => setTimeout(r, 200));
                }

                // If in-page resolution succeeded for all links:
                if (!cfBlocked && directLinks.length === currentLinks.length) {
                    if (statusEl) statusEl.textContent = `Pushing ${directLinks.length} parts to NetDownloader...`;
                    await sendToNetDownloader(directLinks, pkgName);
                    copyToClipboard(directLinks.join('\n'));

                    if (statusEl) statusEl.innerHTML = `<span style="color:#10B981; font-weight:bold;">🎉 All ${directLinks.length} parts sent to NetDownloader!</span>`;
                    setTimeout(() => {
                        resolveBtn.disabled = false;
                        resolveBtn.style.opacity = '1';
                        if (statusEl) statusEl.textContent = 'Resolves direct CDN links automatically';
                    }, 4000);
                    return;
                }

                // STEP 2: Fallback to single worker tab with queue in URL hash
                if (statusEl) statusEl.textContent = `Opening 1 worker tab to bypass Cloudflare...`;
                showToast(`🚀 Opening 1 worker tab to resolve all ${currentLinks.length} parts...`);

                const payloadObj = {
                    queue: currentLinks,
                    pkg: pkgName
                };
                const hashParam = encodeURIComponent(JSON.stringify(payloadObj));
                const workerUrl = currentLinks[0].split('#')[0] + '#nd_batch=' + hashParam;

                // Also save to storage as secondary backup
                if (typeof GM_setValue !== 'undefined') {
                    GM_setValue('netdownloader_batch_queue', currentLinks);
                    GM_setValue('netdownloader_package_name', pkgName);
                }

                window.open(workerUrl, '_blank');

                setTimeout(() => {
                    resolveBtn.disabled = false;
                    resolveBtn.style.opacity = '1';
                    if (statusEl) statusEl.textContent = `Worker tab is running in background...`;
                }, 4000);
            };
        }

        setInterval(createOrUpdateUI, 1000);
    }
})();
