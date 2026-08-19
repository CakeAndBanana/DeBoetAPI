'use client';

import { useTransition } from 'react';
import { useRouter } from 'next/navigation';
import {
  Alert, Button, Card, Group, NumberInput, Select, Stack, Switch, Textarea, TextInput,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { createCategory, updateCategory, type CategoryInput } from './actions';

interface Props {
  options: { value: string; label: string }[];
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
      slug: initial?.slug ?? '',
      metaTitle: initial?.metaTitle ?? '',
      metaDescription: initial?.metaDescription ?? '',
      parentId: initial?.parentId ?? null,
      sortOrder: initial?.sortOrder ?? 0,
      isActive: initial?.isActive ?? true,
      isMenu: initial?.isMenu ?? true,
    },
    validate: { name: (v) => (v.trim() ? null : 'Required') },
  });

  function submit(values: CategoryInput) {
    startTransition(async () => {
      const payload = { ...values, slug: values.slug?.trim() || undefined };
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
          {hasChildren && (
            <Alert color="yellow" title="Renaming rewrites URLs below this category">
              The route is built from names, so changing this one also changes the route of
              every category beneath it. Existing links to those pages will break.
            </Alert>
          )}

          <TextInput label="Name" withAsterisk {...form.getInputProps('name')} />

          <TextInput
            label="Slug"
            description="Left blank, it is derived from the name. Separators are stripped — a dash only ever joins levels."
            placeholder="christmaslights"
            {...form.getInputProps('slug')}
          />

          {!id && (
            <Select
              label="Parent"
              placeholder="(root category)"
              data={options}
              clearable
              searchable
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

          <NumberInput label="Sort order" {...form.getInputProps('sortOrder')} />

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
