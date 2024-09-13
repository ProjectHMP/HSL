const HTTPS = require('https');
const URL = require('url').URL;
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const revisions = getRevisionsData();
const ZipHeader = [0x50, 0x4B, 0x03, 0x04]; // PK
const HMP_SERVER_URL = "https://happinessmp.net/docs/server/getting-started/#download";
const HMP_SERVER_REGEX = /(https:\/\/happinessmp\.net\/files\/[A-Za-z0-9%_\.]*.zip)/g;
const jszip = require('./dist/jszip.js');
const VERSIONS_FOLDER = [__dirname, 'versions'].join(path.sep);
const IGNORED_MATCH = ['resources/', 'settings.xml'];

function getRevisionsData() {
    try {
        return require('./versions.json');
    } catch { return { latest: null, hashes: {}, updated: Date.now() }; }
}

function fetch(url, options = {}) {
    url = new URL(url);
    options.hostname = url.hostname;
    options.port = url.protocol == "https:" ? 443 : 80;
    options.path = url.pathname;
    options.method = options.method ?? "GET"
    return new Promise((resolve, reject) => {
        const request = HTTPS.request(options, (response) => {
            const buffer = [];
            response.on('data', d => buffer.push(d));
            response.on('end', () => resolve(Buffer.concat(buffer)));
        });
        request.on('error', reject);
        request.end();
    });
}

async function main() {

	if(!(await fs.existsSync(VERSIONS_FOLDER))) {
		await fs.mkdirSync(VERSIONS_FOLDER);
	}

    const DOM_buffer = await fetch(HMP_SERVER_URL);

    if (DOM_buffer == null || DOM_buffer.length == 0) {
        throw "Failed to scrape server URL. (What did you do Kitty?!? lol)"
    }
	
    const matches = HMP_SERVER_REGEX.exec(DOM_buffer.toString());
    if (matches == null || matches.length == 0) {
        throw "Failed to find URL for latest server zip. (What did you do Kitty?!? lol)";
    }

	async function fetchHashes(url){
		const buffer = await fetch(url);
        if (buffer == null || buffer.length <= 4 || buffer[0] != ZipHeader[0] || buffer[1] != ZipHeader[1] || buffer[2] != ZipHeader[2] || buffer[3] != ZipHeader[3]) {
            throw "Failed to obtain a proper binary from server, url: " + matches[0];
        }
		const hash = crypto.createHash('md5').update(buffer).digest('hex');
        if (!revisions.hashes.hasOwnProperty(hash)) {
            revisions.hashes[hash] = { url: url, size: buffer.length };
			const d = await jszip.loadAsync(buffer);
			const files = [];
			var root = null;
			for(var name in d.files){
				root ??= name;
				const _buffer = d.file(name);
				if(_buffer == null || IGNORED_MATCH.some(x => name.indexOf(x) >= 0)) {
					continue;
				}
				files.push({
					name: name.substr(root.length),
					hash: crypto.createHash('md5').update(await _buffer.async('nodebuffer')).digest('hex')
				});
			}
			await fs.writeFileSync([VERSIONS_FOLDER, hash + '.json'].join(path.sep), JSON.stringify(files));
        }
		return hash;
	}

    // check if any versions already have this URL, saves time and bandwidth by initially comparing binary hashes.
    if (!Object.keys(revisions.hashes).some(hash => revisions.hashes[hash].url == matches[0])) {
		const hash = await fetchHashes(matches[0]);
		if(hash != null){
			revisions.latest = hash;
		}
    }
	
	revisions.updated = Date.now();
	await fs.writeFileSync([__dirname, "versions.json"].join(path.sep), JSON.stringify(revisions));
}

// entry point (for async reasons)
main();
