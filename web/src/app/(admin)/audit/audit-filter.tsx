'use client';

import { useRouter } from 'next/navigation';
import { Select } from '@mantine/core';

export function AuditFilter({ value }: { value: string }) {
  const router = useRouter();

  return (
    <Select
      label="Record type"
      placeholder="All"
      w={200}
      clearable
      value={value || null}
      data={[
        { value: 'Category', label: 'Categories' },
        { value: 'Item', label: 'Items' },
        { value: 'User', label: 'Users' },
      ]}
      onChange={(next) => router.push(next ? `/audit?entityType=${next}` : '/audit')}
    />
  );
}
