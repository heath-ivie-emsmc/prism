// Copies the production build into the API's wwwroot so one container serves both.
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const source = resolve(here, '..', 'dist', 'web', 'browser');
const target = resolve(here, '..', '..', 'src', 'Prism.Api', 'wwwroot');

if (!existsSync(join(source, 'index.html'))) {
  console.error(`No build found at ${source}. Run "ng build" first.`);
  process.exit(1);
}

// Only ever clear a directory that is the API's own wwwroot.
if (existsSync(target)) {
  for (const entry of readdirSync(target)) rmSync(join(target, entry), { recursive: true, force: true });
} else {
  mkdirSync(target, { recursive: true });
}
cpSync(source, target, { recursive: true });
console.log(`Copied ${source} to ${target}`);
