// Génère les fichiers .meta du paquet. Sans eux, Unity ignore les fichiers d'un paquet installé
// depuis Git (dossier immuable). À relancer après avoir ajouté un fichier hors de Unity :
//
//   node Tools~/generate-meta.mjs
//
// Les GUID sont dérivés du chemin : relancer le script ne change pas ceux des fichiers existants.
import { createHash } from 'node:crypto';
import { readdirSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const guid = (path) => createHash('md5').update('cd.fomapay.unity/' + path).digest('hex');
const tail = '  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n';

function importer(name) {
  if (name.endsWith('.cs')) {
    return 'MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n'
      + '  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n';
  }
  if (name.endsWith('.asmdef')) return 'AssemblyDefinitionImporter:\n' + tail;
  if (name === 'package.json') return 'PackageManifestImporter:\n' + tail;
  return 'TextScriptImporter:\n' + tail;
}

let count = 0;
function walk(dir, relative) {
  for (const name of readdirSync(dir).sort()) {
    // Unity ignore les dossiers terminés par « ~ » (Samples~, Tests~, Tools~) et les fichiers cachés.
    if (name.endsWith('~') || name.endsWith('.meta') || name.startsWith('.')) continue;
    const path = join(dir, name);
    const relativePath = relative ? `${relative}/${name}` : name;
    const isDirectory = statSync(path).isDirectory();
    const body = isDirectory ? 'folderAsset: yes\nDefaultImporter:\n' + tail : importer(name);
    writeFileSync(`${path}.meta`, `fileFormatVersion: 2\nguid: ${guid(relativePath)}\n${body}`);
    count++;
    if (isDirectory) walk(path, relativePath);
  }
}

walk(root, '');
console.log(`${count} fichiers .meta générés`);
