'use client';

import { useTransition } from 'react';
import { useRouter } from 'next/navigation';
import {
  Button, Card, Group, Stack, Switch, Textarea, TextInput,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import type { CategoryOption } from '@/lib/categories';
import { CategoryPicker } from '@/components/category-picker';
import { useT } from '@/i18n/client';
import { createCategory, updateCategory, type CategoryInput } from './actions';

interface Props {
  options: CategoryOption[];
  /** Present when editing. */
  id?: string;
  initial?: Partial<CategoryInput>;
}

export function CategoryForm({ options, id, initial }: Props) {
  const router = useRouter();
  const t = useT();
  const [pending, startTransition] = useTransition();

  const form = useForm<CategoryInput>({
    initialValues: {
      name: initial?.name ?? '',
      slug: initial?.slug ?? '',
      metaTitle: initial?.metaTitle ?? '',
      metaDescription: initial?.metaDescription ?? '',
      parentId: initial?.parentId ?? null,
      // Not shown, but still submitted: the API's SortOrder has no default, so leaving it
      // out of the payload would silently reset an existing category's ordering to 0.
      sortOrder: initial?.sortOrder ?? 0,
      isActive: initial?.isActive ?? true,
      isMenu: initial?.isMenu ?? true,
    },
    validate: { name: (v) => (v.trim() ? null : t('common.required')) },
  });

  function submit(values: CategoryInput) {
    startTransition(async () => {
      // The slug travels with the payload now. On create the field is not rendered, so it
      // is blank and the API derives one from the name; on edit it carries whatever is in the
      // box, which means renaming no longer silently rewrites an established route.
      const result = id ? await updateCategory(id, values) : await createCategory(values);

      if (!result.ok) {
        notifications.show({ color: 'red', title: t('common.refused'), message: result.message! });
        return;
      }

      notifications.show({ color: 'green', title: t('common.saved'), message: t('categories.savedMessage') });
      router.push('/categories');
      router.refresh();
    });
  }

  return (
    <Card withBorder maw={640}>
      <form onSubmit={form.onSubmit(submit)}>
        <Stack>
          <TextInput label={t('categories.fieldName')} withAsterisk {...form.getInputProps('name')} />

          {/*
            Optional on both forms. Left alone it derives from the name, but a route may need
            to be spelled a particular way — including with hyphens, which the derived form
            never produces — and that has to be correctable before the category exists as well
            as after, since a duplicate route is refused at creation.
          */}
          <TextInput
            label={t('categories.fieldSlug')}
            placeholder={id ? undefined : t('categories.fieldSlugPlaceholder')}
            description={t('categories.fieldSlugHelp')}
            {...form.getInputProps('slug')}
          />

          {!id && (
            <CategoryPicker
              label={t('categories.fieldParent')}
              placeholder={t('categories.fieldParentPlaceholder')}
              description={t('categories.fieldParentHelp')}
              options={options}
              {...form.getInputProps('parentId')}
            />
          )}

          <TextInput
            label={t('categories.fieldMetaTitle')}
            description={t('categories.fieldMetaTitleHelp')}
            {...form.getInputProps('metaTitle')}
          />
          <Textarea
            label={t('categories.fieldMetaDesc')}
            description={t('categories.fieldMetaDescHelp')}
            autosize
            minRows={2}
            {...form.getInputProps('metaDescription')}
          />

          <Group>
            <Switch
              label={t('categories.fieldActive')}
              description={t('categories.fieldActiveHelp')}
              {...form.getInputProps('isActive', { type: 'checkbox' })}
            />
            <Switch
              label={t('categories.fieldMenu')}
              description={t('categories.fieldMenuHelp')}
              {...form.getInputProps('isMenu', { type: 'checkbox' })}
            />
          </Group>

          <Group>
            <Button type="submit" loading={pending}>{t('common.save')}</Button>
            <Button variant="subtle" onClick={() => router.push('/categories')}>Cancel</Button>
          </Group>
        </Stack>
      </form>
    </Card>
  );
}
