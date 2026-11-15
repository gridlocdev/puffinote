// Notebook file handling on Node, shared by the desktop app, the NAS server and the CLI.
export {
  overwriteInPlace,
  overwriteInPlaceSync,
  replaceFile,
  replaceFileSync,
  syncDir,
  tempPath,
  TEMP_FILE,
  writeAtomic,
  type InPlaceHandle,
  type InPlaceHandleSync,
} from './atomicWrite.ts';
export { FileError, type FileErrorCode } from './errors.ts';
export { listFolder, mapLimit, moveEntry, type FolderEntry } from './folders.ts';
export { FileLocks } from './locks.ts';
export { isInside, realOfNearest, realTarget } from './paths.ts';
