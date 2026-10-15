import { mkdirSync, readFileSync, existsSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { compileLibraryMigrations } from './library-migrations/compile-migrations.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const catalog = compileLibraryMigrations(root);
if (Buffer.byteLength(JSON.stringify(catalog)) > 1024 * 1024) throw new Error('Library migration catalog exceeds its 1 MiB budget.');
const contractPath = resolve(root, 'src/shared/library/librarySchemaContract.ts');
const contract = '// Generated from the authoritative SQL migrations. Run scripts/generate-library-migrations.mjs --write-contract.\n'
  + 'export const LIBRARY_SCHEMA_DIGEST = ' + JSON.stringify(catalog.digest) + ';\n'
  + 'export const LIBRARY_SCHEMA_MIGRATION_IDS: readonly number[] = ' + JSON.stringify(catalog.migrations.map(item => item.id)) + ';\n';
const writeChanged = (path, contents) => {
  if (existsSync(path) && readFileSync(path, 'utf8').replace(/\r\n/g, '\n') === contents) return;
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, contents);
};
if (process.argv.includes('--write-contract')) writeChanged(contractPath, contract);
else if (!existsSync(contractPath) || readFileSync(contractPath, 'utf8').replace(/\r\n/g, '\n') !== contract)
  throw new Error('Library schema contract is stale. Regenerate it before building Core.');
const outputIndex = process.argv.indexOf('--output');
if (outputIndex >= 0) {
  if (!process.argv[outputIndex + 1]) throw new Error('--output requires a path.');
  writeChanged(resolve(process.argv[outputIndex + 1]), JSON.stringify(catalog));
}
console.log('[library-migrations] ' + catalog.migrations.length + ' migrations compiled from shared SQL definitions.');
