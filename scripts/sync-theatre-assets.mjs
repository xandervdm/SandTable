import { cp, mkdir, readdir, readFile, rm, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const theatresRoot = path.join(repoRoot, "content", "theatres");
const publicTheatresRoot = path.join(repoRoot, "frontend", "public", "theatres");

const theatreEntries = await readdir(theatresRoot, { withFileTypes: true });
for (const entry of theatreEntries.filter((candidate) => candidate.isDirectory()).sort((left, right) => left.name.localeCompare(right.name))) {
  const theatreRoot = path.join(theatresRoot, entry.name);
  const manifestPath = path.join(theatreRoot, "theatre.json");
  let manifest;
  try {
    manifest = JSON.parse(await readFile(manifestPath, "utf8"));
  } catch (error) {
    if (error?.code === "ENOENT") {
      continue;
    }
    throw new Error(`theatre.json: could not read ${entry.name}: ${error.message}`);
  }

  if (manifest.contractVersion !== "sandtable-content-v2") {
    throw new Error(`theatre.json: contractVersion for '${entry.name}' must be 'sandtable-content-v2'.`);
  }
  if (manifest.theatreId !== entry.name) {
    throw new Error(`theatre.json: theatreId '${manifest.theatreId}' must match directory '${entry.name}'.`);
  }

  const assetCatalogPath = resolveInside(theatreRoot, manifest.files?.assets, "theatre.json files.assets");
  const catalog = JSON.parse(await readFile(assetCatalogPath, "utf8"));
  if (!Array.isArray(catalog.assets) || catalog.assets.length === 0) {
    throw new Error(`${manifest.files.assets}: assets must contain at least one asset.`);
  }

  const declaredIds = new Set();
  const declaredSources = new Set();
  const declaredRelativePaths = new Set();
  for (const [index, asset] of catalog.assets.entries()) {
    if (!asset.assetId || declaredIds.has(asset.assetId)) {
      throw new Error(`${manifest.files.assets}: assets[${index}].assetId is missing or duplicated.`);
    }
    declaredIds.add(asset.assetId);

    const sourcePath = resolveInside(theatreRoot, asset.file, `${manifest.files.assets} assets[${index}].file`);
    const assetsRoot = path.resolve(theatreRoot, "assets") + path.sep;
    if (!sourcePath.startsWith(assetsRoot)) {
      throw new Error(`${manifest.files.assets}: assets[${index}].file must be inside the theatre assets directory.`);
    }
    if (declaredSources.has(sourcePath.toLowerCase())) {
      throw new Error(`${manifest.files.assets}: assets[${index}].file duplicates '${asset.file}'.`);
    }
    declaredSources.add(sourcePath.toLowerCase());
    declaredRelativePaths.add(normalizeRelative(path.relative(theatreRoot, sourcePath)));

    const sourceStats = await stat(sourcePath).catch(() => null);
    if (!sourceStats?.isFile()) {
      throw new Error(`${manifest.files.assets}: assets[${index}].file references missing file '${asset.file}'.`);
    }
  }

  const authoredFiles = await listFiles(path.join(theatreRoot, "assets"), theatreRoot);
  for (const authoredFile of authoredFiles) {
    if (!declaredRelativePaths.has(authoredFile)) {
      throw new Error(`${manifest.files.assets}: assets does not declare asset file '${authoredFile}'.`);
    }
  }

  const generatedTheatreRoot = path.resolve(publicTheatresRoot, manifest.theatreId);
  if (!generatedTheatreRoot.startsWith(path.resolve(publicTheatresRoot) + path.sep)) {
    throw new Error(`Refusing to write outside frontend/public/theatres for '${manifest.theatreId}'.`);
  }
  await rm(generatedTheatreRoot, { recursive: true, force: true });

  for (const asset of catalog.assets) {
    const sourcePath = resolveInside(theatreRoot, asset.file, `${manifest.files.assets} asset.file`);
    const destinationPath = resolveInside(generatedTheatreRoot, asset.file, `${manifest.files.assets} asset.file`);
    await mkdir(path.dirname(destinationPath), { recursive: true });
    await cp(sourcePath, destinationPath);
  }

  process.stdout.write(`Synced ${catalog.assets.length} asset(s) for ${manifest.theatreId}.\n`);
}

function resolveInside(root, relativePath, field) {
  if (typeof relativePath !== "string" || relativePath.trim() === "" || path.isAbsolute(relativePath)) {
    throw new Error(`${field} must be a relative path.`);
  }
  const resolvedRoot = path.resolve(root);
  const resolvedPath = path.resolve(root, relativePath);
  if (!resolvedPath.startsWith(resolvedRoot + path.sep)) {
    throw new Error(`${field} escapes its package directory.`);
  }
  return resolvedPath;
}

async function listFiles(directory, relativeRoot) {
  const entries = await readdir(directory, { withFileTypes: true }).catch((error) => {
    if (error?.code === "ENOENT") {
      throw new Error(`Theatre asset directory '${directory}' does not exist.`);
    }
    throw error;
  });
  const files = [];
  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await listFiles(fullPath, relativeRoot));
    } else if (entry.isFile()) {
      files.push(normalizeRelative(path.relative(relativeRoot, fullPath)));
    }
  }
  return files;
}

function normalizeRelative(value) {
  return value.split(path.sep).join("/");
}
