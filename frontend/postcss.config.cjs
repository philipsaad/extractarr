const path = require('path');

const mixinsFiles = [
  path.join(__dirname, 'src/styles/Mixins/cover.css'),
  path.join(__dirname, 'src/styles/Mixins/linkOverlay.css'),
  path.join(__dirname, 'src/styles/Mixins/scroller.css'),
  path.join(__dirname, 'src/styles/Mixins/truncate.css'),
  path.join(__dirname, 'src/styles/Mixins/visuallyHidden.css')
];

module.exports = {
  plugins: {
    autoprefixer: {},
    'postcss-mixins': {
      mixinsFiles
    },
    '@csstools/postcss-global-data': {
      files: [path.join(__dirname, 'src/styles/Variables/breakpoints.css')]
    },
    'postcss-custom-media': {},
    'postcss-nested': {}
  }
};

