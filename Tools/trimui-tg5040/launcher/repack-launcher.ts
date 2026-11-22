// Add one Zircon launcher card to the TrimUI (TG5040) PocketShell boot package.
//
// The 640x360 launcher UI that ships on this card was built from a PocketShell
// source tree that is no longer present on this machine; only the packaged
// launcher exists. This tool therefore repacks the *installed* package, keeping
// its identity, build plan and profile hash byte-identical and replacing only
// the JavaScript section with a copy that carries one extra card.
//
// The card is a regular `external` entry, the same kind the launcher builds for
// installed `/storage/apps/<id>.pocket` packages: it renders a cover through the
// runtime's COVER bridge and launches through the runtime's LAUNCH arm, which
// hands the id to `System/compat/bin/pocketjs-native-launch`.
//
// Usage (from anywhere):
//   bun Tools/trimui-tg5040/launcher/repack-launcher.ts \
//     --source <installed boot.pocket> --out <patched boot.pocket>
//
// Optional: --sdk <PocketJS checkout>; defaults to $PJS_SDK or
// ~/development/pjs-app-build.

import { readFileSync, writeFileSync } from "node:fs";
import { createHash } from "node:crypto";
import { homedir } from "node:os";
import { join, resolve } from "node:path";

const CARD_ID = "zircon";
const CARD_MARKER = '  // trimui-tg5040: Zircon card';

const CARD_SNIPPET = `  var LAUNCHER_APPS = [{
    id: "zircon",
    nameKey: "",
    shortKey: "",
    descKey: "",
    tagKey: "catalog.games",
    kind: "external",
    category: "games",
    icon: "\uf11b",
    label: "Zircon",
    coverPath: "/storage/Apps/zircon.png"
  }, {
    id: "files",`;

function usage(message?: string): never {
  if (message) console.error(`repack-launcher: ${message}`);
  console.error(
    "usage: bun Tools/trimui-tg5040/launcher/repack-launcher.ts " +
      "--source <boot.pocket> --out <patched.pocket> [--sdk <PocketJS checkout>]",
  );
  process.exit(2);
}

function argument(name: string): string | undefined {
  const index = process.argv.indexOf(`--${name}`);
  return index >= 0 ? process.argv[index + 1] : undefined;
}

const sourcePath = argument("source");
const outputPath = argument("out");
if (!sourcePath || !outputPath) usage();
const sdkRoot = resolve(
  argument("sdk") ?? process.env.PJS_SDK ?? join(homedir(), "development/pjs-app-build"),
);
const specPath = join(sdkRoot, "contracts/spec/pocket-package.ts");

const spec = await import(specPath);
const { decodePocketPackage, encodePocketPackage, findSection, POCKET_SECTION } = spec;

const sha256 = (bytes: Uint8Array) => createHash("sha256").update(bytes).digest("hex");

const sourceBytes = new Uint8Array(readFileSync(sourcePath));
const pkg = decodePocketPackage(sourceBytes);
const variant = pkg.variants.find((item: { target: string }) => item.target === "trimui-tsp-fbdev")
  ?? pkg.variants[0];
if (!variant) usage("package has no variants");

const jsSection = findSection(variant, POCKET_SECTION.js);
if (!jsSection) usage("package has no JavaScript section");
const planSection = findSection(variant, POCKET_SECTION.plan);
if (!planSection) usage("package has no build-plan section");

const decoder = new TextDecoder();
const js = decoder.decode(jsSection).replace(/\0$/, "");

// The card is added once; a package that already carries it is left untouched
// apart from the normal re-encode (which is byte-stable for these inputs).
const anchor = '  var LAUNCHER_APPS = [{\n    id: "files",';
let patched = js;
if (patched.includes('id: "zircon"') && patched.includes(CARD_MARKER)) {
  console.log("repack-launcher: card already present, re-encoding only");
} else if (patched.includes(CARD_SNIPPET)) {
  console.log("repack-launcher: card already present, re-encoding only");
} else {
  if (patched.split(anchor).length !== 2) {
    usage("could not find the LAUNCHER_APPS anchor exactly once");
  }
  patched = patched.replace(anchor, CARD_SNIPPET);
  if (!patched.includes('id: "zircon"')) usage("card insertion failed");
  console.log(`repack-launcher: added "${CARD_ID}" card to LAUNCHER_APPS`);
}

// Keep the QuickJS NUL terminator the runtime expects on the JS section.
const encodedJs = new TextEncoder().encode(patched);
const patchedJs = new Uint8Array(encodedJs.length + 1);
patchedJs.set(encodedJs, 0);

const variants = pkg.variants.map((item: { target: string }) => ({
  ...item,
  sections: item.sections.map((section: { kind: number; bytes: Uint8Array }) =>
    section.kind === POCKET_SECTION.js ? { kind: section.kind, bytes: patchedJs } : section,
  ),
}));

const outputBytes = encodePocketPackage({ manifest: pkg.manifest, variants });

// Admission facts must not drift: only the JS payload may differ.
const outPkg = decodePocketPackage(outputBytes);
const outVariant = outPkg.variants.find((item: { target: string }) => item.target === variant.target);
if (!outVariant) usage("repacked package lost its variant");
if (outVariant.profileHash !== variant.profileHash) usage("profile hash changed");
if (sha256(findSection(outVariant, POCKET_SECTION.plan)!)
    !== sha256(findSection(variant, POCKET_SECTION.plan)!)) usage("build plan changed");
if (findSection(outVariant, POCKET_SECTION.identity)
    && sha256(findSection(outVariant, POCKET_SECTION.identity)!)
      !== sha256(findSection(variant, POCKET_SECTION.identity)!)) usage("identity changed");
if (sha256(findSection(outVariant, POCKET_SECTION.pak)!)
    !== sha256(findSection(variant, POCKET_SECTION.pak)!)) usage("asset bundle changed");
if (!decoder.decode(findSection(outVariant, POCKET_SECTION.js)!).includes('id: "zircon"')) {
  usage("repacked JavaScript lost the card");
}

writeFileSync(outputPath, outputBytes);
const plan = JSON.parse(decoder.decode(findSection(outVariant, POCKET_SECTION.plan)!));
console.log(`repack-launcher: ${sourcePath} -> ${outputPath}`);
console.log(`  target ${outVariant.target} · profile ${outVariant.profileHash}`);
console.log(`  app ${plan.app.id} (${plan.app.title}) · abi ${outVariant.hostAbi}`);
console.log(`  bytes ${outputBytes.length} · sha256 ${sha256(outputBytes)}`);
