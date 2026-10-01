// Builds one of Kubuno Core Web's npm packages from the host app's sources, for the package projects of
// Kubuno.Core.Web.slnx (docs/WEB.md, "Packages"):
//
//   node kubuno-packages.mjs <frontend folder> <ui|sdk|drive>
//
//   1. the declarations of the whole host app (tsc -p tsconfig.emit.json -> frontend/dist-types, LF line endings),
//      emitted once and reused by the next package of the same build as long as no source is newer;
//   2. the package's type tree, assembled as core/frontend/packages/build.sh does (ui: ui/; sdk: sdk/ + core/;
//      drive: drive/);
//   3. for @kubuno/ui, the ESM library, with frontend/vite.uilib.config.ts (its externals are an explicit list; the
//      package's own vite.config.ts decides "external" by the shape of the module id, which on Windows - absolute
//      ids such as Z:/... - would leave everything but the entry outside the bundle).
//
// Everything goes to the package's obj\package folder: the committed packages/<id>/types and packages/<id>/dist are
// never rewritten from Windows. Regenerating and publishing the npm packages stays build.sh / _tools/publish_all.sh,
// run by the developer.
import { spawnSync } from 'node:child_process'
import { cpSync, existsSync, mkdirSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'

const [frontend, id] = process.argv.slice(2)
const trees = {
  ui: [['ui', 'types']],
  sdk: [['sdk', 'types/sdk'], ['core', 'types/core']],
  drive: [['drive', 'types/drive']],
}
if (!frontend || !trees[id]) {
  console.error('usage: node kubuno-packages.mjs <frontend folder> <ui|sdk|drive>')
  process.exit(2)
}

const run = (args, cwd) => {
  console.log(`> node ${args.join(' ')}`)
  const result = spawnSync(process.execPath, args, { cwd, stdio: 'inherit' })
  if (result.status !== 0) {
    console.error(`error: node ${args[0]} exited with ${result.status}`)
    process.exit(result.status ?? 1)
  }
}

const newest = (folder) => {
  let time = 0
  for (const entry of readdirSync(folder, { withFileTypes: true })) {
    const path = join(folder, entry.name)
    time = Math.max(time, entry.isDirectory() ? newest(path) : statSync(path).mtimeMs)
  }
  return time
}

// 1. One declaration emit per change of the sources (frontend/dist-types is ignored by git).
const types = join(frontend, 'dist-types')
const stamp = join(types, '.kubuno-emit')
const sourcesTime = Math.max(newest(join(frontend, 'src')), ...['tsconfig.json', 'tsconfig.app.json', 'tsconfig.emit.json']
  .map((file) => join(frontend, file)).filter(existsSync).map((file) => statSync(file).mtimeMs))
if (!existsSync(stamp) || statSync(stamp).mtimeMs < sourcesTime) {
  console.log('Kubuno: emitting the host app declarations (tsc -p tsconfig.emit.json)')
  rmSync(types, { recursive: true, force: true })
  run([join(frontend, 'node_modules', 'typescript', 'bin', 'tsc'), '-p', 'tsconfig.emit.json', '--newLine', 'lf'], frontend)
  writeFileSync(stamp, new Date().toISOString())
} else {
  console.log('Kubuno: host app declarations up to date')
}

// 2. The package's type tree, in obj\package.
const output = join(frontend, 'packages', id, 'obj', 'package')
rmSync(output, { recursive: true, force: true })
for (const [from, to] of trees[id]) {
  mkdirSync(join(output, to), { recursive: true })
  cpSync(join(types, from), join(output, to), { recursive: true })
}
console.log(`Kubuno: @kubuno/${id} types assembled in ${output}`)

// 3. The @kubuno/ui ESM bundle, in obj\package\dist.
if (id === 'ui') {
  run([join(frontend, 'node_modules', 'vite', 'bin', 'vite.js'), 'build', '--config', 'vite.uilib.config.ts',
    '--outDir', join(output, 'dist'), '--emptyOutDir'], frontend)
}
