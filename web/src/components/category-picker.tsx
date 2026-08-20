'use client';

import { Group, Select, Text } from '@mantine/core';
import { IconCheck } from '@tabler/icons-react';
import type { CategoryOption } from '@/lib/categories';
import { useT } from '@/i18n/client';

interface Props {
  label: string;
  placeholder: string;
  description?: string;
  /** Optional so `form.getInputProps('parentId')` can be spread straight in. */
  value?: string | null;
  onChange: (value: string | null) => void;
  onFocus?: React.FocusEventHandler<HTMLInputElement>;
  onBlur?: React.FocusEventHandler<HTMLInputElement>;
  options: CategoryOption[];
  /** Ids that must not be offered — a category cannot be moved into its own subtree. */
  exclude?: Set<string>;
  error?: React.ReactNode;
}

/**
 * The one way to pick a category anywhere in the panel.
 *
 * Deliberately a `Select` and not an `Autocomplete` or a creatable combobox: the only valid
 * answers are categories that already exist, so typing must filter the list, never invent an
 * entry. Whatever is typed is discarded unless it resolves to an option.
 *
 * `limit` matters at scale. A few hundred categories is an ordinary size for this tree, and
 * rendering every one of them into the dropdown makes opening it visibly slow — the search
 * narrows the list before it is rendered, and the cap keeps the DOM small either way.
 */
export function CategoryPicker({
  label, placeholder, description, value, onChange, onFocus, onBlur, options, exclude, error,
}: Props) {
  const t = useT();
  const data = exclude?.size ? options.filter((o) => !exclude.has(o.value)) : options;
  const routes = new Map(data.map((o) => [o.value, o.pathSlug]));

  return (
    <Select
      label={label}
      description={description}
      placeholder={placeholder}
      data={data}
      value={value ?? null}
      onChange={onChange}
      onFocus={onFocus}
      onBlur={onBlur}
      error={error}
      clearable
      searchable
      limit={100}
      maxDropdownHeight={320}
      nothingFoundMessage={t('picker.noMatch')}
      // Names repeat across branches, so the route is searchable too: typing "christmas ice"
      // or "icelights" finds it even when the name alone is ambiguous.
      filter={({ options: items, search, limit: max }) => {
        const terms = search.trim().toLowerCase().split(/\s+/).filter(Boolean);

        const matches = items.filter((item) => {
          if (!('value' in item)) return false;
          if (terms.length === 0) return true;
          const haystack = `${item.label} ${routes.get(item.value) ?? ''}`.toLowerCase();
          return terms.every((term) => haystack.includes(term));
        });

        // Replacing the default filter means replacing its capping too.
        return matches.slice(0, max);
      }}
      // A custom option replaces Mantine's, tick included, so the current choice is drawn here.
      renderOption={({ option, checked }) => (
        <Group gap="xs" wrap="nowrap" style={{ flex: 1, minWidth: 0 }}>
          <div style={{ flex: 1, minWidth: 0 }}>
            <Text size="sm" truncate>{option.label}</Text>
            <Text size="xs" c="dimmed" truncate>{routes.get(option.value)}</Text>
          </div>
          {checked && <IconCheck size={16} stroke={2} style={{ flexShrink: 0 }} />}
        </Group>
      )}
    />
  );
}
