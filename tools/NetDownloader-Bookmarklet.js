javascript:(function(){
    const serverUrl = 'http://127.0.0.1:9666/api/add';
    
    // 1. If on fuckingfast.co or a file hoster
    if (window.location.hostname.includes('fuckingfast.co')) {
        const candidate = Array.from(document.querySelectorAll('a, button')).find(el => {
            const h = el.getAttribute('href') || '';
            const t = (el.innerText || '').toLowerCase();
            return h.includes('/dl/') || t.includes('download');
        });
        const link = candidate ? (candidate.href || candidate.getAttribute('href')) : window.location.href;
        fetch(serverUrl, {
            method: 'POST',
            headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({
                packageName: window.location.hash.replace('#', '') || document.title,
                urls: [link]
            })
        }).then(() => alert('✅ Sent direct link to NetDownloader!'))
          .catch(() => alert('⚠️ Make sure NetDownloader is running!'));
        return;
    }

    // 2. If on a repack page (e.g. FitGirl)
    const links = Array.from(document.querySelectorAll('a'))
        .map(a => a.href)
        .filter(h => h && (h.includes('fuckingfast.co') || h.includes('.part') || h.includes('.rar')));
    
    if (links.length === 0) {
        alert('No download links found on this page.');
        return;
    }

    fetch(serverUrl, {
        method: 'POST',
        headers: {'Content-Type': 'application/json'},
        body: JSON.stringify({
            packageName: document.title,
            urls: links
        })
    }).then(() => alert(`✅ Sent ${links.length} links to NetDownloader!`))
      .catch(() => alert('⚠️ Make sure NetDownloader is running!'));
})();
