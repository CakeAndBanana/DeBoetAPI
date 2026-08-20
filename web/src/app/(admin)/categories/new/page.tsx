import { Stack, Title } from '@mantine/core';
import { flatten, getTree } from '@/lib/categories';
import { getT } from '@/i18n/server';
import { CategoryForm } from '../category-form';

export default async function NewCategoryPage({
  searchParams,
}: {
  searchParams: Promise<{ parent?: string }>;
}) {
  const t = await getT();
  const { parent } = await searchParams;
  const options = flatten(await getTree());

  return (
    <Stack>
      <Title order={2}>{t('categories.newTitle')}</Title>
      <CategoryForm options={options} initial={{ parentId: parent ?? null }} />
    </Stack>
  );
}
