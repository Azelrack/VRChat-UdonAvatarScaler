How to do a new release ?
1. Check that everything compiles and runs properly
2. Update package version in package.json
3. Merge to master using --no-ff tag to create a merge commit
4. Properly tag this commit using the same tag as the one updated in package.json
5. Push
6. Create a new release on github that includes an export of .unitypackage and zip
7. Launch the job in the VPM listing project that rebuilds listing