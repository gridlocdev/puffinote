export {
  type DecodedText,
  encodeText,
  isReadOnlyDecode,
  latin1,
  readText,
  readTextAs,
  type TextDecodeConfidence,
  type TextEncodingName,
  type TextFileFormat,
  utf8NoBom,
} from './document/codec.ts';
export { countGraphemes, nextBoundary, previousBoundary } from './document/grapheme.ts';
export {
  describeSearchStatus,
  findAll,
  isValidRegex,
  type SearchMatch,
  type SearchOptions,
} from './document/search.ts';
export {
  countWords,
  foldedWords,
  type FoldedText,
  foldString,
  foldWithOffsets,
  isEmptyQuery,
  parseWordQuery,
  WordIndex,
  type WordMatch,
  wordMatches,
  type WordQuery,
} from './document/wordSearch.ts';
export {
  caretAt,
  isCollapsed,
  type LinePosition,
  type Selection,
  selectionEnd,
  selectionIntersects,
  selectionLength,
  selectionsEqual,
  selectionStart,
} from './document/selection.ts';
export {
  computeStats,
  countLineEndings,
  type DocumentStats,
  type LineEndingCounts,
  newlineLabel,
  type NewlineStyle,
  normalizeLineEndings,
} from './document/stats.ts';
export { type DocumentChange, TextDocument } from './document/textDocument.ts';
export {
  type BufferListener,
  deleteBackRun,
  deleteForwardRun,
  DocumentBuffer,
} from './editing/documentBuffer.ts';
export { type UndoEffect, type UndoRun } from './editing/editHistory.ts';
export { type ReloadEdit, reloadEdit } from './editing/reloadEdit.ts';
export { applyEdit, compose, invertEdit, type TextEdit, touches } from './editing/textEdit.ts';
export { ignoreError, ignoreErrorSync } from './errors/ignoreError.ts';
