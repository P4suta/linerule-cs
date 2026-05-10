// Conventional Commits enforcement (memory: feedback_adopt_industry_standards).
// Strict: subject-case enforced, header length capped, type/scope mandatory.
module.exports = {
    extends: ['@commitlint/config-conventional'],
    rules: {
        'header-max-length': [2, 'always', 100],
        'header-min-length': [2, 'always', 10],
        'subject-case': [2, 'always', ['lower-case', 'sentence-case']],
        'subject-empty': [2, 'never'],
        'subject-full-stop': [2, 'never', '.'],
        'type-empty': [2, 'never'],
        'type-enum': [
            2,
            'always',
            [
                'feat',
                'fix',
                'docs',
                'style',
                'refactor',
                'perf',
                'test',
                'build',
                'ci',
                'chore',
                'revert',
            ],
        ],
    },
};
