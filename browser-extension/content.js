// NetDownloader Chromium Extension - Content Script
'use strict';

(function () {
  const HOSTNAME = window.location.hostname;

  // 1. Message listener from popup / background
  chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
    if (request.action === 'EXTRACT_PAGE_LINKS') {
      const result = extractLinks();
      sendResponse(result);
    }
  });

  // 2. Helper: Extract links safely from page
  function extractLinks() {
    // A. PrivateBin paste page check
    if (HOSTNAME.includes('paste.fitgirl-repacks.site')) {
      const statusEl = document.getElementById('status');
      if (statusEl && statusEl.offsetParent !== null && statusEl.textContent && statusEl.textContent.includes('Decrypting')) {
        return { isStillDecrypting: true, links: [], packageName: '' };
      }
    }

    // B. Single file host page check
    if (HOSTNAME.includes('fuckingfast.co')) {
      const pathMatch = window.location.pathname.match(/\/(?:f\/)?([a-zA-Z0-9]+)/);
      if (pathMatch && pathMatch[1] && !['speed', 'index', 'help'].includes(pathMatch[1])) {
        const pkg = window.location.hash.replace(/^#/, '') || document.querySelector('h1, h2')?.textContent?.trim() || pathMatch[1];
        return {
          links: [window.location.href],
          packageName: pkg
        };
      }
    }

    // C. Scan page content & anchor links
    const bodyText = document.body ? (document.body.innerText || document.body.textContent || '') : '';
    const anchors = Array.from(document.querySelectorAll('a'));
    return parseLinks(bodyText, anchors);
  }

  function parseLinks(text, anchors) {
    const urlRegex = /(?:https?:\/\/)?(?:[a-zA-Z0-9-]+\.)?fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)(?:#[^\s"'<>]*)?/gi;
    const matches = text ? (text.match(urlRegex) || []) : [];

    const anchorHrefs = (anchors || [])
      .map(a => a.href)
      .filter(h => h && h.includes('fuckingfast.co/'));

    const combined = [...matches, ...anchorHrefs];
    const cleaned = Array.from(new Set(combined.map(url => {
      let u = url.trim().replace(/[.,;'">\)\]]+$/, '');
      if (!u.startsWith('http://') && !u.startsWith('https://')) {
        u = 'https://' + u;
      }
      return u;
    })));

    return {
      links: cleaned,
      packageName: derivePackageName(cleaned)
    };
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
    return document.title.replace(/[\-\|].*$/, '').trim() || 'NetDownloader_Package';
  }

  // 3. Floating In-Page Widget (Encapsulated in Shadow DOM)
  function initInPageWidget() {
    if (document.getElementById('nd-shadow-host')) return;

    // Check if on pastebin or main site
    if (HOSTNAME.includes('fitgirl-repacks.site')) {
      const data = extractLinks();
      if (!data || data.links.length === 0 || data.isStillDecrypting) return;

      renderFitGirlWidget(data.links, data.packageName);
    } else if (HOSTNAME.includes('fuckingfast.co')) {
      const pathMatch = window.location.pathname.match(/\/(?:f\/)?([a-zA-Z0-9]+)/);
      if (pathMatch && pathMatch[1] && !['speed', 'index', 'help'].includes(pathMatch[1])) {
        renderFileHostWidget(pathMatch[1]);
      }
    }
  }

  function createShadowHost() {
    let host = document.getElementById('nd-shadow-host');
    if (!host) {
      host = document.createElement('div');
      host.id = 'nd-shadow-host';
      host.style.position = 'fixed';
      host.style.bottom = '24px';
      host.style.right = '24px';
      host.style.zIndex = '2147483647';
      document.body.appendChild(host);
    }
    return host.shadowRoot || host.attachShadow({ mode: 'open' });
  }

  function renderFitGirlWidget(links, pkgName) {
    const shadow = createShadowHost();
    shadow.innerHTML = `
      <style>
        .nd-card {
          background: #181D28;
          border: 1px solid #2D8CFF;
          border-radius: 12px;
          padding: 14px 18px;
          box-shadow: 0 10px 30px rgba(0,0,0,0.6);
          font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
          color: #FFFFFF;
          min-width: 320px;
          max-width: 420px;
        }
        .nd-top {
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: 6px;
        }
        .nd-brand {
          font-weight: 700;
          font-size: 14px;
          color: #2D8CFF;
          display: flex;
          align-items: center;
          gap: 6px;
        }
        .nd-badge {
          background: #222733;
          color: #10B981;
          font-size: 11px;
          font-weight: 700;
          padding: 2px 8px;
          border-radius: 6px;
          border: 1px solid #10B981;
        }
        .nd-name {
          font-size: 12px;
          color: #94A3B8;
          margin-bottom: 12px;
          white-space: nowrap;
          overflow: hidden;
          text-overflow: ellipsis;
        }
        .nd-btns {
          display: flex;
          gap: 8px;
        }
        .nd-btn-primary {
          flex: 1;
          background: #2D8CFF;
          color: #FFFFFF;
          border: none;
          border-radius: 8px;
          padding: 9px 12px;
          font-weight: bold;
          font-size: 12px;
          cursor: pointer;
        }
        .nd-btn-secondary {
          background: #222733;
          color: #CBD5E1;
          border: 1px solid #334155;
          border-radius: 8px;
          padding: 9px 12px;
          font-size: 12px;
          cursor: pointer;
        }
        .nd-status {
          font-size: 11px;
          color: #64748B;
          margin-top: 8px;
          text-align: center;
        }
      </style>
      <div class="nd-card">
        <div class="nd-top">
          <div class="nd-brand"><span>⚡</span> NetDownloader</div>
          <span class="nd-badge">${links.length} parts found</span>
        </div>
        <div class="nd-name" title="${pkgName}">${pkgName}</div>
        <div class="nd-btns">
          <button id="btn-resolve" class="nd-btn-primary">🚀 1-Click Resolve &amp; Download</button>
          <button id="btn-copy" class="nd-btn-secondary" title="Copy raw links">📋 Copy</button>
        </div>
        <div id="nd-status" class="nd-status">Ready to resolve direct CDN links</div>
      </div>
    `;

    const btnResolve = shadow.getElementById('btn-resolve');
    const btnCopy = shadow.getElementById('btn-copy');
    const statusEl = shadow.getElementById('nd-status');

    btnCopy.onclick = async () => {
      await navigator.clipboard.writeText(links.join('\n'));
      statusEl.textContent = `📋 Copied ${links.length} links to clipboard!`;
      statusEl.style.color = '#10B981';
    };

    btnResolve.onclick = () => {
      btnResolve.disabled = true;
      statusEl.textContent = `Resolving ${links.length} parts in background...`;
      statusEl.style.color = '#2D8CFF';

      chrome.runtime.sendMessage({
        action: 'RESOLVE_AND_SEND',
        urls: links,
        packageName: pkgName
      }, (res) => {
        btnResolve.disabled = false;
        if (chrome.runtime.lastError) {
          statusEl.textContent = 'Extension updated — please refresh this page (F5).';
          statusEl.style.color = '#EF4444';
          return;
        }
        if (res && res.success) {
          statusEl.textContent = `🎉 All ${res.count} parts sent to NetDownloader!`;
          statusEl.style.color = '#10B981';
        } else {
          statusEl.textContent = res?.error || 'Failed to send to desktop app.';
          statusEl.style.color = '#EF4444';
        }
      });
    };
  }

  function renderFileHostWidget(fileId) {
    const shadow = createShadowHost();
    const fileName = window.location.hash.replace(/^#/, '') || document.querySelector('h1, h2')?.textContent?.trim() || fileId;

    shadow.innerHTML = `
      <style>
        .nd-card {
          background: #181D28;
          border: 1px solid #2D8CFF;
          border-radius: 12px;
          padding: 14px 18px;
          box-shadow: 0 10px 30px rgba(0,0,0,0.6);
          font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
          color: #FFFFFF;
          min-width: 280px;
          max-width: 360px;
        }
        .nd-top {
          font-weight: 700;
          font-size: 14px;
          color: #2D8CFF;
          margin-bottom: 4px;
        }
        .nd-name {
          font-size: 12px;
          color: #94A3B8;
          margin-bottom: 12px;
          white-space: nowrap;
          overflow: hidden;
          text-overflow: ellipsis;
        }
        .nd-btns {
          display: flex;
          gap: 8px;
        }
        .nd-btn-primary {
          flex: 1;
          background: #2D8CFF;
          color: #FFFFFF;
          border: none;
          border-radius: 6px;
          padding: 9px 12px;
          font-weight: bold;
          font-size: 12px;
          cursor: pointer;
        }
        .nd-btn-secondary {
          background: #222733;
          color: #CBD5E1;
          border: 1px solid #334155;
          border-radius: 6px;
          padding: 9px 10px;
          font-size: 12px;
          cursor: pointer;
        }
        .nd-btn-batch {
          width: 100%;
          margin-top: 8px;
          background: #1E2533;
          color: #2D8CFF;
          border: 1px solid #2D8CFF;
          border-radius: 6px;
          padding: 7px 10px;
          font-weight: 600;
          font-size: 11px;
          cursor: pointer;
        }
        .nd-status {
          font-size: 11px;
          color: #64748B;
          margin-top: 8px;
          text-align: center;
        }
      </style>
      <div class="nd-card">
        <div class="nd-top">⚡ NetDownloader</div>
        <div class="nd-name" title="${fileName}">${fileName}</div>
        <div class="nd-btns">
          <button id="btn-send" class="nd-btn-primary">🚀 Send to NetDownloader</button>
          <button id="btn-copy" class="nd-btn-secondary" title="Copy direct link">📋 Copy</button>
        </div>
        <button id="btn-batch" class="nd-btn-batch">⚡ Paste &amp; Resolve All Parts</button>
        <div id="nd-status" class="nd-status">Single file ready</div>
      </div>
    `;

    const btnSend = shadow.getElementById('btn-send');
    const btnCopy = shadow.getElementById('btn-copy');
    const btnBatch = shadow.getElementById('btn-batch');
    const statusEl = shadow.getElementById('nd-status');

    btnSend.onclick = () => {
      btnSend.disabled = true;
      statusEl.textContent = 'Resolving direct CDN link...';

      chrome.runtime.sendMessage({
        action: 'RESOLVE_AND_SEND',
        urls: [window.location.href],
        packageName: fileName
      }, (res) => {
        btnSend.disabled = false;
        if (chrome.runtime.lastError) {
          statusEl.textContent = 'Extension updated — please refresh this page (F5).';
          statusEl.style.color = '#EF4444';
          return;
        }
        if (res && res.success) {
          statusEl.textContent = '✅ Queued in NetDownloader!';
          statusEl.style.color = '#10B981';
        } else {
          statusEl.textContent = res?.error || 'Failed to send to app.';
          statusEl.style.color = '#EF4444';
        }
      });
    };

    btnCopy.onclick = () => {
      chrome.runtime.sendMessage({
        action: 'RESOLVE_SINGLE',
        url: window.location.href
      }, async (res) => {
        if (chrome.runtime.lastError) {
          statusEl.textContent = 'Extension updated — please refresh this page (F5).';
          statusEl.style.color = '#EF4444';
          return;
        }
        const direct = res?.resolvedUrl || window.location.href;
        await navigator.clipboard.writeText(direct);
        statusEl.textContent = '📋 Copied direct link to clipboard!';
        statusEl.style.color = '#10B981';
      });
    };

    btnBatch.onclick = () => {
      const raw = prompt('Paste all part links (from FitGirl Pastebin) here:');
      if (!raw) return;

      const links = raw.match(/https?:\/\/[^\s"'<>]+/gi) || [];
      if (links.length === 0) {
        alert('No valid URLs found in pasted text.');
        return;
      }

      btnBatch.disabled = true;
      statusEl.textContent = `Resolving ${links.length} parts in background...`;
      statusEl.style.color = '#2D8CFF';

      chrome.runtime.sendMessage({
        action: 'RESOLVE_AND_SEND',
        urls: links
      }, (res) => {
        btnBatch.disabled = false;
        if (chrome.runtime.lastError) {
          statusEl.textContent = 'Extension updated — please refresh this page (F5).';
          statusEl.style.color = '#EF4444';
          return;
        }
        if (res && res.success) {
          statusEl.textContent = `🎉 All ${res.count} parts sent to NetDownloader!`;
          statusEl.style.color = '#10B981';
        } else {
          statusEl.textContent = res?.error || 'Could not queue parts.';
          statusEl.style.color = '#EF4444';
        }
      });
    };
  }

  // Periodic check (every 1.5 seconds) - strictly non-blocking!
  setInterval(initInPageWidget, 1500);
})();
