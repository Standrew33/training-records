import fs from 'node:fs';
import path from 'node:path';
import { buildDataset } from '../src/lib/clean';

const dir = path.resolve('data-raw');
const out = path.resolve('public/data/dataset.json');

if (!fs.existsSync(dir)) {
  console.log('data-raw/ neexistuje – používám již vygenerovaný public/data/dataset.json');
  process.exit(0);
}

/** UTF-8, a pokud soubor není validní UTF-8, windows-1250 (absolvovana_skoleni.csv je exportován ve Windows-1250). */
function readText(name: string): string {
  const buf = fs.readFileSync(path.join(dir, name));
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(buf);
  } catch {
    console.log(`${name}: není UTF-8, dekóduji jako windows-1250`);
    return new TextDecoder('windows-1250').decode(buf);
  }
}

const ds = buildDataset(
  {
    zamestnanci: readText('zamestnanci.csv'),
    absolvovana: readText('absolvovana_skoleni.csv'),
    katalog: readText('katalog_skoleni.csv'),
    terminy: readText('terminy_skoleni.csv'),
  },
  '2026-10-01',
);
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, JSON.stringify(ds));
const by = (s: string) => ds.issues.filter((i) => i.severity === s).length;
console.log(`OK: ${ds.employees.length} zaměstnanců, ${ds.completions.length} školení, ${ds.terms.length} termínů; problémy: ${by('chyba')} chyb, ${by('varování')} varování, ${by('info')} info`);
