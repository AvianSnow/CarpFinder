// Writes .agent/kctg-trade-goods.txt from Kris's Compendium of Trade Goods (A5e edition),
// https://github.com/Syrious/kctg-a5e, by kristkos and Syrious. Used with Kris's permission: names, prices,
// weights and descriptions only. None of the module's art may be used, and Kris (Syrious) must be credited.
//
// A Foundry compendium pack is a LevelDB folder, so this needs Node.js and one package:
//   git clone --depth 1 https://github.com/Syrious/kctg-a5e /tmp/kctg
//   mkdir /tmp/kctg-tool && cd /tmp/kctg-tool && npm init -y && npm install classic-level
//   cp <repo>/.agent/port-scripts/kctg_extract.mjs . && node kctg_extract.mjs /tmp/kctg <repo>/.agent/kctg-trade-goods.txt
import { ClassicLevel } from 'classic-level';
import fs from 'fs';
import os from 'os';
import path from 'path';

const [, , repo, out] = process.argv;
const manifest = JSON.parse(fs.readFileSync(path.join(repo, 'module.json'), 'utf8'));

function text(html) {
    return (html ?? '')
        .replace(/<\/p>\s*<p[^>]*>/g, ' ')
        .replace(/<br\s*\/?>/g, ' ')
        .replace(/<[^>]+>/g, '')
        .replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>')
        .replace(/&quot;/g, '"').replace(/&#39;|&apos;|&rsquo;/g, "'")
        .replace(/\s+/g, ' ').trim();
}

const lines = [
    "Kris's Compendium of Trade Goods (KCTG), A5e edition " + manifest.version,
    'By kristkos (https://patreon.com/KCTG), ported to Foundry A5e by Syrious (https://github.com/Syrious/kctg-a5e).',
    'Used in CarpFinder with permission from Kris (Syrious), who must be credited.',
    'Only the names, prices, weights and descriptions are used. None of the module\'s art may be used.',
    '',
    'Extracted by .agent/port-scripts/kctg_extract.mjs. Do not edit by hand.',
    'Prices are as printed, in 5e coins: 1 pp = 10 gp, 1 gp = 2 ep = 10 sp = 100 cp. Weights are in pounds.',
    'Which of these prices CarpFinder items use is in .agent/port-scripts/trade_prices.py.',
    '',
    'Each item is:  Name | price | weight | kind',
    '               description',
];

for (const pack of manifest.packs) {
    // Opening a LevelDB writes to it, so read a copy.
    const copy = fs.mkdtempSync(path.join(os.tmpdir(), 'kctg-'));
    fs.cpSync(path.join(repo, pack.path), copy, { recursive: true });
    const db = new ClassicLevel(copy, { keyEncoding: 'utf8', valueEncoding: 'utf8' });
    await db.open();
    const folders = new Map();
    const items = [];
    for await (const [key, value] of db.iterator()) {
        const doc = JSON.parse(value);
        if (key.startsWith('!folders!')) folders.set(doc._id, doc);
        else if (key.startsWith('!items!')) items.push(doc);
    }
    await db.close();
    fs.rmSync(copy, { recursive: true, force: true });

    const folderPath = id => {
        const names = [];
        for (let f = folders.get(id); f; f = folders.get(f.folder)) names.unshift(f.name);
        return names.join(' / ') || '(no folder)';
    };
    const groups = new Map();
    for (const item of items) {
        const where = folderPath(item.folder);
        if (!groups.has(where)) groups.set(where, []);
        groups.get(where).push(item);
    }

    lines.push('', '#'.repeat(100), `# PACK: ${pack.label} (${items.length} items)`, '#'.repeat(100));
    for (const where of [...groups.keys()].sort()) {
        const list = groups.get(where).sort((a, b) => a.name.localeCompare(b.name));
        lines.push('', `== ${where} (${list.length}) ==`, '');
        for (const item of list) {
            const s = item.system;
            const price = `${s.price.value} ${s.price.denomination}` + (s.price.special ? ` (${s.price.special})` : '');
            lines.push(`${item.name} | ${price} | ${s.weight} lb | ${s.objectType}`);
            lines.push(`    ${text(s.description) || '(no description)'}`);
        }
    }
}

fs.writeFileSync(out, lines.join('\n') + '\n');
console.log(`wrote ${out}: ${lines.length} lines`);
