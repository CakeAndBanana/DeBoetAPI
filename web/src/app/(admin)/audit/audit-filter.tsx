'use client';

import { useRouter } from 'next/navigation';
import { Button, Group, Select } from '@mantine/core';
import { IconFilterOff } from '@tabler/icons-react';
import type { MessageKey } from '@/i18n';
import { useT } from '@/i18n/client';

const RECORD_TYPES = ['Category', 'Item', 'User'] as const;

const ACTION_GROUPS = {
  'audit.groupRecords': [
    'Created', 'Updated', 'Deleted', 'Moved', 'ChildPromoted',
    'LinkAdded', 'LinkRemoved', 'CategoriesAssigned',
  ],
  'audit.groupAccounts': [
    'PasswordReset', 'PasswordChanged', 'RolesChanged', 'Disabled', 'Enabled',
  ],
} as const;



export interface AuditFilterValues {
  entityType: string;
  action: string;
  changedById: string;
}

export function AuditFilter({
  values, people,
}: {
  values: AuditFilterValues;
  /** Everyone who could have made a change, so the person filter is pickable rather than typed. */
  people: { value: string; label: string }[];
}) {
  const router = useRouter();
  const t = useT();
  const anyApplied = !!(values.entityType || values.action || values.changedById);

  // Filters combine, and changing one drops you back to page 1 — page 3 of the old result set
  // is rarely page 3 of the new one, and is often past its end.
  function apply(patch: Partial<AuditFilterValues>) {
    const next = { ...values, ...patch };
    const params = new URLSearchParams();
    for (const [key, value] of Object.entries(next)) {
      if (value) params.set(key, value);
    }

    const query = params.toString();
    router.push(query ? `/audit?${query}` : '/audit');
  }

  return (
    <Group align="flex-end" gap="sm">
      <Select
        label={t('audit.recordType')}
        placeholder={t('common.all')}
        w={150}
        clearable
        value={values.entityType || null}
        data={RECORD_TYPES.map((value) => ({
          value,
          label: t(`audit.${value.toLowerCase()}s` as MessageKey),
        }))}
        onChange={(next) => apply({ entityType: next ?? '' })}
      />

      {/* Grouped so a long flat list does not hide the one action someone is after — the
          taxonomy actions and the account actions are rarely wanted together. */}
      <Select
        label={t('audit.action')}
        placeholder={t('common.all')}
        w={200}
        clearable
        searchable
        value={values.action || null}
        data={Object.entries(ACTION_GROUPS).map(([group, actions]) => ({
          group: t(group as MessageKey),
          items: actions.map((value) => ({
            value,
            label: t(`action.${value}` as MessageKey),
          })),
        }))}
        onChange={(next) => apply({ action: next ?? '' })}
      />

      <Select
        label={t('audit.person')}
        placeholder={t('common.anyone')}
        w={180}
        clearable
        searchable
        value={values.changedById || null}
        data={people}
        nothingFoundMessage={t('audit.noSuchAccount')}
        onChange={(next) => apply({ changedById: next ?? '' })}
      />

      {anyApplied && (
        <Button
          variant="subtle"
          leftSection={<IconFilterOff size={16} />}
          onClick={() => router.push('/audit')}
        >
          {t('common.clear')}
        </Button>
      )}
    </Group>
  );
}
