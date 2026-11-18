// NetDownloader Chromium Extension - Popup Controller
'use strict';

document.addEventListener('DOMContentLoaded', async () => {
  const statusBadge = document.getElementById('status-badge');
  const statusText = document.getElementById('status-text');

  const tabBtnPage = document.getElementById('tab-page');
  const tabBtnBatch = document.getElementById('tab-batch');
  const panelPage = document.getElementById('panel-page');
  const panelBatch = document.getElementById('panel-batch');

  const pageCountBadge = document.getElementById('page-count-badge');
  const pagePkgName = document.getElementById('page-pkg-name');
  const btnPageResolve = document.getElementById('btn-page-resolve');
  const btnPageCopy = document.getElementById('btn-page-copy');
  const pageLog = document.getElementById('page-log');

  const batchInput = document.getElementById('batch-input');
  const batchPkg = document.getElementById('batch-pkg');
  const btnBatchResolve = document.getElementById('btn-batch-resolve');
  const progressContainer = document.getElementById('progress-container');
  const progressBarFill = document.getElementById('progress-bar-fill');
  const progressText = document.getElementById('progress-text');

  const footerNotif = document.getElementById('footer-notification');
  const footerMsg = document.getElementById('footer-msg');

  let detectedLinks = [];
  let detectedPackageName = '';

  let isAppOnline = false;

  // 1. Check NetDownloader desktop app connection
  const statusFallbackTimer = setTimeout(() => {
    if (statusText.textContent === 'Checking...') {
      statusBadge.className = 'status-badge offline';
      statusText.textContent = 'App Offline';
    }
  }, 1000);

  chrome.runtime.sendMessage({ action: 'CHECK_STATUS' }, (res) => {
    clearTimeout(statusFallbackTimer);
    if (res && res.isOnline) {
      isAppOnline = true;
      statusBadge.className = 'status-badge online';
      statusText.textContent = `Online (${res.port})`;
    } else {
      isAppOnline = false;
      statusBadge.className = 'status-badge offline';
      statusText.textContent = 'App Offline';
    }
  });

  // 2. Tab Navigation
  tabBtnPage.addEventListener('click', () => {
    tabBtnPage.classList.add('active');
    tabBtnBatch.classList.remove('active');
    panelPage.classList.add('active');
    panelBatch.classList.remove('active');
  });

  tabBtnBatch.addEventListener('click', () => {
    tabBtnBatch.classList.add('active');
    tabBtnPage.classList.remove('active');
    panelBatch.classList.add('active');
    panelPage.classList.remove('active');
  });

  // Helper: Extract links from active tab with scripting fallback if content script not loaded
  async function extractLinksFromTab(tabId) {
    return new Promise((resolve) => {
      chrome.tabs.sendMessage(tabId, { action: 'EXTRACT_PAGE_LINKS' }, (response) => {
        if (!chrome.runtime.lastError && response) {
          resolve(response);
          return;
        }

        // Fallback: inject extraction via chrome.scripting
        try {
          chrome.scripting.executeScript({
            target: { tabId: tabId },
            func: () => {
              const statusEl = document.getElementById('status');
              if (statusEl && statusEl.offsetParent !== null && statusEl.textContent && statusEl.textContent.includes('Decrypting')) {
                return { isStillDecrypting: true, links: [], packageName: '' };
              }

              const urlRegex = /(?:https?:\/\/)?(?:[a-zA-Z0-9-]+\.)?fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)(?:#[^\s"'<>]*)?/gi;
              const bodyText = document.body ? (document.body.innerText || document.body.textContent || '') : '';
              const matches = bodyText.match(urlRegex) || [];

              const anchorHrefs = Array.from(document.querySelectorAll('a'))
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

              let pkg = '';
              for (const link of cleaned) {
                if (link.includes('#')) {
                  const hash = link.split('#')[1];
                  if (hash) {
                    let c = hash.replace(/[\._\- ]+part\d+[\._\- ]*(?:\.rar|\.zip|\.7z|\.001)?$/i, '');
                    c = c.replace(/\.(?:rar|zip|7z|001)$/i, '');
                    if (c.length > 2) { pkg = c; break; }
                  }
                }
              }
              if (!pkg) pkg = document.title.replace(/[\-\|].*$/, '').trim() || 'NetDownloader_Package';

              return { links: cleaned, packageName: pkg, isStillDecrypting: false };
            }
          }, (injectionResults) => {
            if (injectionResults && injectionResults[0] && injectionResults[0].result) {
              resolve(injectionResults[0].result);
            } else {
              resolve({ links: [], packageName: '', isStillDecrypting: false });
            }
          });
        } catch (err) {
          resolve({ links: [], packageName: '', isStillDecrypting: false });
        }
      });
    });
  }

  // 3. Inspect Current Active Tab
  try {
    const [activeTab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (activeTab && activeTab.id) {
      const response = await extractLinksFromTab(activeTab.id);

      if (response && response.links && response.links.length > 0) {
        detectedLinks = response.links;
        detectedPackageName = response.packageName || 'NetDownloader_Package';

        pageCountBadge.textContent = `${detectedLinks.length} parts`;
        pagePkgName.textContent = detectedPackageName;
        pagePkgName.title = detectedPackageName;

        btnPageResolve.disabled = false;
        btnPageCopy.disabled = false;
        pageLog.textContent = `Found ${detectedLinks.length} parts ready to resolve & download.`;
      } else if (response && response.isStillDecrypting) {
        pageLog.textContent = 'Pastebin is still decrypting... Please wait a moment.';
      } else {
        pageLog.textContent = 'No archive or host links found on this page.';
      }
    }
  } catch (e) {
    pageLog.textContent = 'Could not inspect active tab.';
  }

  // 4. Current Page Action: 1-Click Resolve & Download
  btnPageResolve.addEventListener('click', async () => {
    if (detectedLinks.length === 0) return;

    if (!isAppOnline) {
      const checkRes = await new Promise((r) => chrome.runtime.sendMessage({ action: 'CHECK_STATUS' }, r));
      if (!checkRes || !checkRes.isOnline) {
        showToast('⚠️ NetDownloader is not running! Launch NetDownloader.Gui.exe first.', 'error');
        return;
      }
      isAppOnline = true;
      statusBadge.className = 'status-badge online';
      statusText.textContent = `Online (${checkRes.port})`;
    }

    btnPageResolve.disabled = true;
    btnPageResolve.textContent = 'Resolving links...';
    showToast('Resolving all parts with preserved filenames...', 'info');

    chrome.runtime.sendMessage({
      action: 'RESOLVE_AND_SEND',
      urls: detectedLinks,
      packageName: detectedPackageName
    }, (result) => {
      btnPageResolve.disabled = false;
      btnPageResolve.textContent = '🚀 1-Click Resolve & Download';

      if (chrome.runtime.lastError || !result) {
        showToast('Extension background timed out or failed.', 'error');
        return;
      }

      if (result.success) {
        showToast(`✅ Successfully sent ${result.count} parts to NetDownloader!`, 'success');
      } else {
        showToast(result.error || 'Could not send to desktop app.', 'error');
      }
    });
  });

  // 5. Current Page Action: Copy Direct Links
  btnPageCopy.addEventListener('click', async () => {
    if (detectedLinks.length === 0) return;

    btnPageCopy.disabled = true;
    btnPageCopy.textContent = 'Resolving...';

    chrome.runtime.sendMessage({
      action: 'RESOLVE_ONLY',
      urls: detectedLinks
    }, async (result) => {
      btnPageCopy.disabled = false;
      btnPageCopy.textContent = '📋 Copy Direct Links';

      if (!chrome.runtime.lastError && result && result.resolvedUrls && result.resolvedUrls.length > 0) {
        await navigator.clipboard.writeText(result.resolvedUrls.join('\n'));
        showToast(`📋 Copied ${result.resolvedUrls.length} direct links to clipboard!`, 'success');
      } else {
        await navigator.clipboard.writeText(detectedLinks.join('\n'));
        showToast(`📋 Copied raw links to clipboard!`, 'success');
      }
    });
  });

  // 6. Batch Paste Action: Resolve All & Send
  btnBatchResolve.addEventListener('click', async () => {
    const rawText = batchInput.value.trim();
    if (!rawText) {
      showToast('Please paste at least one link.', 'error');
      return;
    }

    const urls = rawText.match(/https?:\/\/[^\s"'<>]+/gi) || [];
    if (urls.length === 0) {
      showToast('No valid URLs found in text.', 'error');
      return;
    }

    // Verify desktop app is running
    if (!isAppOnline) {
      const checkRes = await new Promise((r) => chrome.runtime.sendMessage({ action: 'CHECK_STATUS' }, r));
      if (!checkRes || !checkRes.isOnline) {
        showToast('⚠️ NetDownloader is not running! Launch NetDownloader.Gui.exe first.', 'error');
        return;
      }
      isAppOnline = true;
    }

    const pkgName = batchPkg.value.trim() || undefined;

    btnBatchResolve.disabled = true;
    btnBatchResolve.textContent = 'Resolving...';

    progressContainer.classList.remove('hidden');
    progressBarFill.style.width = '30%';
    progressText.textContent = `Resolving ${urls.length} parts in background...`;

    chrome.runtime.sendMessage({
      action: 'RESOLVE_AND_SEND',
      urls: urls,
      packageName: pkgName
    }, (result) => {
      btnBatchResolve.disabled = false;
      btnBatchResolve.textContent = '⚡ Resolve All & Send to NetDownloader';

      if (chrome.runtime.lastError || !result) {
        progressContainer.classList.add('hidden');
        showToast('Error connecting to background or desktop app.', 'error');
        return;
      }

      progressBarFill.style.width = '100%';
      progressText.textContent = `Completed! ${urls.length} parts processed.`;

      if (result && result.success) {
        showToast(`✅ Successfully queued ${result.count} parts in NetDownloader!`, 'success');
      } else {
        showToast(result?.error || 'Failed to send to NetDownloader desktop app.', 'error');
      }

      setTimeout(() => {
        progressContainer.classList.add('hidden');
        progressBarFill.style.width = '0%';
      }, 5000);
    });
  });

  // Helper: Toast Notifications
  function showToast(message, type = 'info') {
    footerMsg.textContent = message;
    footerNotif.className = `footer-notification ${type}`;
    footerNotif.classList.remove('hidden');

    setTimeout(() => {
      footerNotif.classList.add('hidden');
    }, 4000);
  }
});
