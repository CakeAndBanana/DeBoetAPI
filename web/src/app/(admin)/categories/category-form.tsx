'use client';

import { useTransition } from 'react';
import { useRouter } from 'next/navigation';
import {
  Alert, Button, Card, Group, Stack, Switch, Textarea, TextInput,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import type { CategoryOption } from '@/lib/categories';
import { CategoryPicker } from '@/components/category-picker';
import { createCategory, updateCategory, type CategoryInput } from './actions';

interface Props {
  options: CategoryOption[];
  /** Present when editing. */
  id?: string;
  initial?: Partial<CategoryInput>;
  /** Editing a category that has children warns about route rewrites. */
  hasChildren?: boolean;
}

export function CategoryForm({ options, id, initial, hasChildren }: Props) {
  const router = useRouter();
  const [pending, startTransition] = useTransition();

  const form = useForm<CategoryInput>({
    initialValues: {
      name: initial?.name ?? '',
      metaTitle: initial?.metaTitle ?? '',
      metaDescription: initial?.metaDescription ?? '',
      parentId: initial?.parentId ?? null,
      // Not shown, but still submitted: the API's SortOrder has no default, so leaving it
      // out of the payload would silently reset an existing category's ordering to 0.
      sortOrder: initial?.sortOrder ?? 0,
      isActive: initial?.isActive ?? true,
      isMenu: initial?.isMenu ?? true,
    },
    validate: { name: (v) => (v.trim() ? null : 'Required') },
  });

  function submit(values: CategoryInput) {
    startTransition(async () => {
      // No slug: omitting it makes the API derive one from the name, every time.
      const payload = { ...values, slug: undefined };
      const result = id ? await updateCategory(id, payload) : await createCategory(payload);

      if (!result.ok) {
        notifications.show({ color: 'red', title: 'Refused', message: result.message! });
        return;
      }

      notifications.show({ color: 'green', title: 'Saved', message: 'Category saved.' });
      router.push('/categories');
      router.refresh();
    });
  }

  return (
    <Card withBorder maw={640}>
      <form onSubmit={form.onSubmit(submit)}>
        <Stack>
          {id && (
            <Alert color="yellow" title="Renaming changes the URL">
              The slug is always derived from the name, so renaming changes this
              category&apos;s route
              {hasChildren ? ' and the route of every category beneath it' : ''}. Existing
              links will break.
            </Alert>
          )}

          <TextInput label="Name" withAsterisk {...form.getInputProps('name')} />

          {!id && (
            <CategoryPicker
              label="Parent"
              placeholder="(root category)"
              description="Search by name or route."
              options={options}
              {...form.getInputProps('parentId')}
            />
          )}

          <TextInput
            label="Meta title"
            description="Falls back to the name when blank."
            {...form.getInputProps('metaTitle')}
          />
          <Textarea
            label="Meta description"
            description="Optional. Left blank, search engines write their own snippet."
            autosize
            minRows={2}
            {...form.getInputProps('metaDescription')}
          />

          <Group>
            <Switch
              label="Active"
              description="Hidden from listings when off"
              {...form.getInputProps('isActive', { type: 'checkbox' })}
            />
            <Switch
              label="In menu"
              description="Appears in navigation"
              {...form.getInputProps('isMenu', { type: 'checkbox' })}
            />
          </Group>

          <Group>
            <Button type="submit" loading={pending}>Save</Button>
            <Button variant="subtle" onClick={() => router.push('/categories')}>Cancel</Button>
          </Group>
        </Stack>
      </form>
    </Card>
  );
}
