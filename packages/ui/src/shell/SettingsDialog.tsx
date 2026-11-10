import { useState } from 'preact/hooks';
import type { Notebook } from '../notebook/notebook.ts';
import {
  defaultSettings,
  settingFields,
  type PartialSettings,
  type PrefsStorage,
  type SettingField,
} from '../notebook/settings.ts';
import { Dialog } from './Dialog.tsx';

type Scope = 'user' | 'notebook';

/**
 * Settings (PRD §7.5): two scopes over the same fields. "Preferences" are yours, in every notebook; "This
 * Notebook" pins a value for everyone who opens the folder, in `.puffinote/settings.json`, and a field left
 * on "Use preference" inherits.
 */
export function SettingsDialog(props: {
  readonly nb: Notebook | null;
  readonly prefs: PrefsStorage;
  readonly onClose: () => void;
}) {
  const { nb } = props;
  const [scope, setScope] = useState<Scope>('user');
  const [user, setUser] = useState<PartialSettings>(nb ? { ...nb.userPrefs.peek() } : props.prefs.load());
  const [book, setBook] = useState<PartialSettings>(nb ? { ...nb.overrides.peek() } : {});
  const apply = () => {
    if (nb) {
      nb.setPreferences(user);
      void nb.setOverrides(book);
    } else props.prefs.save(user);
    props.onClose();
  };
  const layer = scope === 'user' ? user : book;
  const setLayer = scope === 'user' ? setUser : setBook;
  return (
    <Dialog
      title="Settings"
      class="pn-settings"
      onClose={props.onClose}
      onSubmit={apply}
      actions={
        <>
          <button type="button" onClick={props.onClose}>
            Cancel
          </button>
          <button type="submit" class="pn-primary">
            Done
          </button>
        </>
      }
    >
      {nb && (
        <div class="pn-segmented" role="tablist">
          <button type="button" role="tab" aria-selected={scope === 'user'} onClick={() => setScope('user')}>
            Preferences
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={scope === 'notebook'}
            onClick={() => setScope('notebook')}
          >
            This Notebook
          </button>
        </div>
      )}
      <p class="pn-settings-scope">
        {scope === 'user'
          ? 'Yours, in every notebook.'
          : `Saved in “${nb?.name}” for everyone who opens it. Anything left on “Use preference” follows each person’s own.`}
      </p>
      <div class="pn-settings-fields">
        {settingFields.map((f) => (
          <Field
            key={f.key}
            field={f}
            value={layer[f.key]}
            inherited={
              scope === 'notebook' ? (user[f.key] ?? defaultSettings[f.key]) : defaultSettings[f.key]
            }
            optional={scope === 'notebook'}
            onChange={(v) => {
              const next: Record<string, unknown> = { ...layer };
              if (v === undefined) delete next[f.key];
              else next[f.key] = v;
              setLayer(next as PartialSettings);
            }}
          />
        ))}
      </div>
    </Dialog>
  );
}

function Field(props: {
  readonly field: SettingField;
  readonly value: unknown;
  readonly inherited: unknown;
  readonly optional: boolean;
  readonly onChange: (v: unknown) => void;
}) {
  const { field } = props;
  const id = `pn-setting-${field.key}`;
  const shown = props.value ?? props.inherited;
  const label = (v: unknown) =>
    field.options ? (field.options.find((o) => o.value === v)?.label ?? String(v)) : v ? 'On' : 'Off';
  const values: readonly unknown[] = field.options ? field.options.map((o) => o.value) : [true, false];
  return (
    <div class="pn-setting">
      <label for={id}>{field.label}</label>
      {!props.optional && !field.options ? (
        <input
          id={id}
          type="checkbox"
          checked={shown === true}
          onChange={(e) => props.onChange((e.target as HTMLInputElement).checked)}
        />
      ) : (
        <select
          id={id}
          value={props.optional ? (props.value === undefined ? '' : String(props.value)) : String(shown)}
          onChange={(e) => {
            const raw = (e.target as HTMLSelectElement).value;
            props.onChange(raw === '' ? undefined : values.find((v) => String(v) === raw));
          }}
        >
          {props.optional && <option value="">Use preference ({label(props.inherited)})</option>}
          {values.map((v) => (
            <option key={String(v)} value={String(v)}>
              {label(v)}
            </option>
          ))}
        </select>
      )}
      <p class="pn-setting-help">{field.help}</p>
    </div>
  );
}
