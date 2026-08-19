import { Stack, Title } from '@mantine/core';
import { flatten, getTree } from '@/lib/categories';
import { CategoryForm } from '../category-form';

export default async function NewCategoryPage({
  searchParams,
}: {
  searchParams: Promise<{ parent?: string }>;
}) {
  const { parent } = await searchParams;
  const options = flatten(await getTree());

  return (
    <Stack>
      <Title order={2}>New category</Title>
      <CategoryForm options={options} initial={{ parentId: parent ?? null }} />
    </Stack>
  );
}
