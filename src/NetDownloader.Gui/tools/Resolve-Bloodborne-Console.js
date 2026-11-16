// Run this script in your browser's Developer Tools Console (F12) on any fuckingfast.co tab:
(async function() {
    const rawLinks = [
        "https://fuckingfast.co/w674u09fbhw0#Bloodborne_--_fitgirl-repacks.site_--_.part1.rar",
        "https://fuckingfast.co/0zfhw0rh79m5#Bloodborne_--_fitgirl-repacks.site_--_.part2.rar",
        "https://fuckingfast.co/ixqnd4zfsewh#Bloodborne_--_fitgirl-repacks.site_--_.part3.rar",
        "https://fuckingfast.co/95bzzlpjvcz4#Bloodborne_--_fitgirl-repacks.site_--_.part4.rar",
        "https://fuckingfast.co/xzvq8ikxtsvk#Bloodborne_--_fitgirl-repacks.site_--_.part5.rar",
        "https://fuckingfast.co/5o8psze1dfd1#Bloodborne_--_fitgirl-repacks.site_--_.part6.rar",
        "https://fuckingfast.co/51lq1l0fnvai#Bloodborne_--_fitgirl-repacks.site_--_.part7.rar",
        "https://fuckingfast.co/5qk2vw6iowfu#Bloodborne_--_fitgirl-repacks.site_--_.part8.rar"
    ];

    console.log("🚀 Resolving all 8 Bloodborne parts directly from fuckingfast.co...");
    const directLinks = [];

    for (let i = 0; i < rawLinks.length; i++) {
        const link = rawLinks[i];
        const match = link.match(/fuckingfast\.co\/(?:f\/)?([a-zA-Z0-9]+)/);
        if (!match) continue;
        const id = match[1];

        try {
            console.log(`Resolving Part ${i + 1} of 8 (ID: ${id})...`);
            
            // Try endpoint 1: /f/{id}/go
            let res = await fetch(`/f/${id}/go`, {
                method: 'POST',
                headers: { 'HX-Request': 'true' },
                credentials: 'include'
            });

            let dlUrl = res.headers.get('hx-redirect') || res.headers.get('HX-Redirect');

            // Try endpoint 2: /{id}
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
                let fullUrl = new URL(dlUrl, window.location.origin).href;
                if (link.includes('#') && !fullUrl.includes('#')) {
                    fullUrl += link.substring(link.indexOf('#'));
                }
                console.log(`✅ Part ${i + 1} Resolved: ${fullUrl}`);
                directLinks.push(fullUrl);
            } else {
                console.warn(`⚠️ Could not resolve Part ${i + 1}, using original`);
                directLinks.push(link);
            }
        } catch (err) {
            console.error(`Error on Part ${i + 1}:`, err);
            directLinks.push(link);
        }

        await new Promise(r => setTimeout(r, 350));
    }

    console.log(`🎉 Successfully resolved direct CDN links!`);

    // Send to NetDownloader ports
    let sent = false;
    for (const port of [9666, 9667, 9668, 9669]) {
        try {
            const resp = await fetch(`http://127.0.0.1:${port}/api/add`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    packageName: "Bloodborne_--_fitgirl-repacks.site_--_",
                    urls: directLinks
                })
            });
            if (resp.ok) {
                console.log(`✅ Successfully sent all 8 links directly to NetDownloader on port ${port}!`);
                sent = true;
                break;
            }
        } catch (e) { }
    }

    // Also copy to clipboard
    try {
        await navigator.clipboard.writeText(directLinks.join('\n'));
        console.log("📋 Copied direct links to clipboard!");
    } catch (e) { }

    if (sent) {
        alert(`✅ Success! All ${directLinks.length} parts resolved and sent straight into NetDownloader!`);
    } else {
        alert(`📋 All ${directLinks.length} direct download links resolved and copied to clipboard! (Paste them into NetDownloader LinkGrabber)`);
    }
})();
