const HTTPS = require('https');
const URL = require('url').URL;
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const revisions = require('./revisions.json');

const HMP_SERVER_URL = "https://happinessmp.net/docs/server/getting-started/#download";
const HMP_SERVER_REGEX = /(https:\/\/happinessmp\.net\/files\/[A-Za-z0-9%_\.]*.zip)/g;

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

async function main(){

	const buffer = await fetch(HMP_SERVER_URL);
	const matches = HMP_SERVER_REGEX.exec(buffer.toString());
	if(matches == null || matches.length == 0){
		return;
	}
	
	const server_zip = matches[0];
	const zip_buffer = await fetch(server_zip);
	
	const hash = crypto.createHash('md5').update(await fetch(server_zip)).digest('hex');

	let update = false;

	if(!revisions.hashes.hasOwnProperty(hash)){
		revisions.hashes[hash] = server_zip;
		update = true;
	}
	
	if(revisions.latest != hash) {
		revisions.latest = hash;
		update = true;
	}
	
	if(update){
		await fs.writeFileSync([__dirname, "revisions.json"].join(path.sep), JSON.stringify(revisions));
	}
	
}

main();