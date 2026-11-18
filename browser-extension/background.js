// NetDownloader Chromium Extension - Background Service Worker
'use strict';

const NETDOWNLOADER_PORTS = [9666, 9667, 9668, 9669];
let lastActivePort = 9666;

// Context Menus Setup
chrome.runtime.onInstalled.addListener(() => {
  if (chrome.contextMenus) {
    chrome.contextMenus.removeAll(() => {
      chrome.contextMenus.create({
        id: 'nd_download_link',
        title: 'Download with NetDownloader',
        contexts: ['link']
      });

      chrome.contextMenus.create({
        id: 'nd_download_selection',
        title: 'Send Selected Links to NetDownloader',
        contexts: ['selection']
      });

      chrome.contextMenus.create({
        id: 'nd_download_page',
        title: 'Grab All Links on Current Page',
        contexts: ['page']
      });
    });
  }

  checkStatus();
});

// Periodic status check (every 30 seconds)
if (chrome.alarms) {
  chrome.alarms.create('check_status_alarm', { periodInMinutes: 0.5 });
  chrome.alarms.onAlarm.addListener((alarm) => {
    if (alarm.name === 'check_status_alarm') {
      checkStatus();
    }
  });
}

// Helper: Check if NetDownloader desktop app is running
async function checkStatus() {
  const promises = NETDOWNLOADER_PORTS.map(async (port) => {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 600);
      const res = await fetch(`http://127.0.0.1:${port}/jdcheck.js`, {
        signal: controller.signal
      });
      clearTimeout(timeoutId);

      if (res.ok) {
        return port;
      }
    } catch (e) { }
    return null;
  });

  const results = await Promise.all(promises);
  const activePort = results.find(p => p !== null);

  if (activePort) {
    lastActivePort = activePort;
    chrome.action.setBadgeText({ text: 'ON' });
    chrome.action.setBadgeBackgroundColor({ color: '#10B981' });
    chrome.action.setTitle({ title: `NetDownloader: Online (Port ${activePort})` });
    return { isOnline: true, port: activePort };
  }

  chrome.action.setBadgeText({ text: '' });
  chrome.action.setTitle({ title: 'NetDownloader: Desktop App Offline' });
  return { isOnline: false, port: null };
}

// Helper: Send URLs directly to NetDownloader desktop API
async function sendToNetDownloader(urls, packageName, destinationDirectory, password) {
  if (!urls || urls.length === 0) {
    return { success: false, error: 'No URLs provided' };
  }

  const payload = {
    packageName: packageName || derivePackageName(urls),
    urls: Array.isArray(urls) ? urls : [urls],
    destinationDirectory: destinationDirectory || null,
    password: password || null
  };

  const portsToTry = [lastActivePort, ...NETDOWNLOADER_PORTS.filter(p => p !== lastActivePort)];

  for (const port of portsToTry) {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 800);
      const res = await fetch(`http://127.0.0.1:${port}/api/add`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
        signal: controller.signal
      });
      clearTimeout(timeoutId);

      if (res.ok) {
        lastActivePort = port;
        chrome.action.setBadgeText({ text: 'ON' });
        chrome.action.setBadgeBackgroundColor({ color: '#10B981' });
        return { success: true, count: payload.urls.length, port };
      }
    } catch (e) { }
  }

  return { success: false, error: 'NetDownloader desktop app is offline. Please launch NetDownloader.Gui.exe first!' };
}

// Helper: Derive package name from link list (e.g. FitGirl / multi-part archive)
function derivePackageName(urls) {
  for (const url of urls) {
    if (url.includes('#')) {
      const hash = url.split('#')[1];
      if (hash) {
        let cleaned = hash.replace(/[\._\- ]+part\d+[\._\- ]*(?:\.rar|\.zip|\.7z|\.001)?$/i, '');
        cleaned = cleaned.replace(/\.(?:rar|zip|7z|001)$/i, '');
        if (cleaned.length > 2) return cleaned;
      }
    }
  }
  return 'NetDownloader_Package';
}

// Helper: Resolve a single file host link to direct CDN URL with preserved filename hash
async function resolveHostLink(url) {
  const match = url.match(/fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)/i);
  if (!match) {
    return { url, cfBlocked: false }; // Non-host link or already direct
  }

  const id = match[1];
  const hashFragment = url.includes('#') ? url.substring(url.indexOf('#')) : '';

  let directUrl = null;
  let cfBlocked = false;

  const tryFetchEndpoint = async (endpointUrl, method = 'POST') => {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 3500);
      const headers = method === 'POST'
        ? { 'HX-Request': 'true', 'Referer': `https://fuckingfast.co/${id}` }
        : { 'Referer': `https://fuckingfast.co/${id}` };

      const res = await fetch(endpointUrl, {
        method,
        headers,
        credentials: 'include',
        signal: controller.signal
      });
      clearTimeout(timeoutId);

      let found = res.headers.get('hx-redirect') || res.headers.get('HX-Redirect');
      if (!found && res.url && res.url.includes('/dl/')) {
        found = res.url;
      }

      const contentType = res.headers.get('content-type') || '';
      if (!found && (contentType.includes('text') || contentType.includes('html') || contentType.includes('json'))) {
        const text = await res.text();
        if (res.status === 403 || text.includes('Just a moment') || text.includes('challenge-running')) {
          cfBlocked = true;
          return null;
        }
        const m = text.match(/https?:\/\/[^\s"'<>]+(?:\/dl\/[^\s"'<>]+|\.rar|\.zip|\.7z)/i);
        if (m) found = m[0];
      } else if (res.status === 403) {
        cfBlocked = true;
      }

      return found;
    } catch (e) {
      return null;
    }
  };

  // Endpoint 1: POST /f/{id}/go
  directUrl = await tryFetchEndpoint(`https://fuckingfast.co/f/${id}/go`, 'POST');

  // Endpoint 2: POST /{id}
  if (!directUrl && !cfBlocked) {
    directUrl = await tryFetchEndpoint(`https://fuckingfast.co/${id}`, 'POST');
  }

  // Endpoint 3: GET /{id} (in case link is embedded in script tag)
  if (!directUrl && !cfBlocked) {
    directUrl = await tryFetchEndpoint(`https://fuckingfast.co/${id}`, 'GET');
  }

  if (directUrl) {
    if (hashFragment && !directUrl.includes('#')) {
      directUrl += hashFragment;
    }
    return { url: directUrl, cfBlocked: false };
  }

  return { url, cfBlocked };
}

// Helper: Resolve batch inside an actual browser tab (bypasses Cloudflare JS/cookie checks)
async function resolveViaBrowserTab(urls) {
  let targetTabId = null;
  let createdTempTab = false;

  try {
    const existingTabs = await chrome.tabs.query({ url: '*://*.fuckingfast.co/*' });
    if (existingTabs && existingTabs.length > 0) {
      targetTabId = existingTabs[0].id;
    } else if (urls.length > 0) {
      const firstClean = urls[0].split('#')[0];
      const newTab = await chrome.tabs.create({ url: firstClean, active: false });
      targetTabId = newTab.id;
      createdTempTab = true;

      // Wait for tab to finish loading (max 8s)
      await new Promise((resolve) => {
        const timer = setTimeout(() => {
          chrome.tabs.onUpdated.removeListener(listener);
          resolve();
        }, 8000);
        const listener = (tabId, changeInfo) => {
          if (tabId === targetTabId && changeInfo.status === 'complete') {
            clearTimeout(timer);
            chrome.tabs.onUpdated.removeListener(listener);
            setTimeout(resolve, 1200);
          }
        };
        chrome.tabs.onUpdated.addListener(listener);
      });
    }

    if (targetTabId) {
      const injection = await chrome.scripting.executeScript({
        target: { tabId: targetTabId },
        func: async (linkList) => {
          const out = [];
          for (const link of linkList) {
            const match = link.match(/fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)/i);
            if (!match) {
              out.push(link);
              continue;
            }
            const id = match[1];
            const hash = link.includes('#') ? link.substring(link.indexOf('#')) : '';
            let dlUrl = null;
            try {
              let res = await fetch(`/f/${id}/go`, { method: 'POST', headers: { 'HX-Request': 'true' }, credentials: 'include' });
              dlUrl = res.headers.get('hx-redirect') || res.headers.get('HX-Redirect');
              if (!dlUrl) {
                res = await fetch(`/${id}`, { method: 'POST', headers: { 'HX-Request': 'true' }, credentials: 'include' });
                dlUrl = res.headers.get('hx-redirect') || res.headers.get('HX-Redirect');
              }
              if (!dlUrl && res.url && res.url.includes('/dl/')) {
                dlUrl = res.url;
              }
              const ct = res.headers.get('content-type') || '';
              if (!dlUrl && (ct.includes('text') || ct.includes('html') || ct.includes('json'))) {
                const txt = await res.text();
                const m = txt.match(/https?:\/\/[^\s"'<>]+(?:\/dl\/[^\s"'<>]+|\.rar|\.zip|\.7z)/i);
                if (m) dlUrl = m[0];
              }
              if (dlUrl) {
                dlUrl = new URL(dlUrl, window.location.origin).href;
                if (hash && !dlUrl.includes('#')) dlUrl += hash;
              }
            } catch (e) { }
            out.push(dlUrl || link);
            await new Promise(r => setTimeout(r, 200));
          }
          return out;
        },
        args: [urls]
      });

      if (injection && injection[0] && Array.isArray(injection[0].result)) {
        return injection[0].result;
      }
    }
  } catch (e) {
  } finally {
    if (createdTempTab && targetTabId) {
      try { await chrome.tabs.remove(targetTabId); } catch (e) { }
    }
  }

  return urls;
}

// Helper: Resolve array of URLs in sequence with Cloudflare tab fallback
async function resolveBatch(urls, onProgress) {
  const results = [];
  for (let i = 0; i < urls.length; i++) {
    const original = urls[i];
    if (typeof onProgress === 'function') {
      onProgress(i + 1, urls.length, original);
    }
    const res = await resolveHostLink(original);
    if (res.cfBlocked) {
      // Fall back to resolving the entire batch via an in-browser tab session
      return await resolveViaBrowserTab(urls);
    }
    results.push(res.url);
    await new Promise(r => setTimeout(r, 150));
  }
  return results;
}

// Context Menu Click Handling
if (chrome.contextMenus) {
  chrome.contextMenus.onClicked.addListener(async (info, tab) => {
    if (info.menuItemId === 'nd_download_link' && info.linkUrl) {
      const res = await resolveHostLink(info.linkUrl);
      await sendToNetDownloader([res.url]);
    } else if (info.menuItemId === 'nd_download_selection' && info.selectionText) {
      const urls = info.selectionText.match(/https?:\/\/[^\s"'<>]+/gi) || [];
      if (urls.length > 0) {
        const resolvedList = await resolveBatch(urls);
        await sendToNetDownloader(resolvedList);
      }
    } else if (info.menuItemId === 'nd_download_page' && tab && tab.id) {
      try {
        chrome.tabs.sendMessage(tab.id, { action: 'EXTRACT_PAGE_LINKS' }, async (response) => {
          if (!chrome.runtime.lastError && response && response.links && response.links.length > 0) {
            const resolvedList = await resolveBatch(response.links);
            await sendToNetDownloader(resolvedList, response.packageName);
          }
        });
      } catch (e) { }
    }
  });
}

// Message Listener for Content Scripts & Popup
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === 'CHECK_STATUS') {
    checkStatus()
      .then(sendResponse)
      .catch(() => sendResponse({ isOnline: false, port: null }));
    return true;
  }

  if (message.action === 'SEND_LINKS') {
    sendToNetDownloader(message.urls, message.packageName, message.destinationDirectory, message.password)
      .then(sendResponse)
      .catch((err) => sendResponse({ success: false, error: err?.message || 'Send failed' }));
    return true;
  }

  if (message.action === 'RESOLVE_AND_SEND') {
    (async () => {
      try {
        const resolvedList = await resolveBatch(message.urls || []);
        const result = await sendToNetDownloader(resolvedList, message.packageName, message.destinationDirectory, message.password);
        sendResponse({ ...result, resolvedUrls: resolvedList });
      } catch (err) {
        sendResponse({ success: false, error: err?.message || 'Resolution failed' });
      }
    })();
    return true;
  }

  if (message.action === 'RESOLVE_ONLY') {
    (async () => {
      try {
        const resolvedList = await resolveBatch(message.urls || []);
        sendResponse({ success: true, resolvedUrls: resolvedList });
      } catch (err) {
        sendResponse({ success: false, resolvedUrls: message.urls || [] });
      }
    })();
    return true;
  }

  if (message.action === 'RESOLVE_SINGLE') {
    resolveHostLink(message.url)
      .then(res => sendResponse({ resolvedUrl: res.url }))
      .catch(() => sendResponse({ resolvedUrl: message.url }));
    return true;
  }
});
